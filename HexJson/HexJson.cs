using System;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Linq;
using System.Collections.Generic;

namespace HexJson
{
    public enum JsonValueType
    {
        Null,
        Boolean,
        Number,
        String,
        Array,
        Object
    };
    public interface IJsonValue
    {
        JsonValueType GetValueType();
    };
    [System.Serializable]
    public class JsonRuntimeException : Exception
    {
        public JsonRuntimeException() { }
        public JsonRuntimeException(string message) : base(message) { }
        public JsonRuntimeException(string message, Exception inner) : base(message, inner) { }
        protected JsonRuntimeException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
    public class JsonValue : IJsonValue
    {
        protected string m_string;
        protected double m_cache;
        protected JsonValueType m_type;
        public JsonValue(string value, double cache, JsonValueType type)
        {
            m_cache = cache;
            m_string = value;
            m_type = type;
        }
        public JsonValueType GetValueType()
        {
            return m_type;
        }
        public double AsDouble()
        {
            if (m_type == JsonValueType.Number)
                return m_cache;
            throw new JsonRuntimeException("Not float");
        }
        public string AsString()
        {
            if (m_type == JsonValueType.String)
                return m_string;
            throw new JsonRuntimeException("Not string");
        }
        public int AsInt()
        {
            if (m_type == JsonValueType.Number)
                return (int)m_cache;
            throw new JsonRuntimeException("Not float");
        }
        public bool AsBoolean()
        {
            if (m_type == JsonValueType.Boolean)
                return m_cache == 1;
            throw new JsonRuntimeException("Not boolean");
        }
        public object AsBoxed()
        {
            if (m_type == JsonValueType.String)
                return m_string;
            else if (m_type == JsonValueType.Boolean)
                return m_cache == 0 ? false : true;
            else if (m_type == JsonValueType.Null)
                return null;
            else
                return m_cache;
        }
        public static bool IsValue(IJsonValue value)
        {
            JsonValueType type = value.GetValueType();
            return (type == JsonValueType.Boolean || type == JsonValueType.Number || type == JsonValueType.Null || type == JsonValueType.String);
        }
        public static JsonValue From(string value)
        {
            return new JsonValue(value, 0, JsonValueType.String);
        }
        public static JsonValue From(double value)
        {
            return new JsonValue(null, value, JsonValueType.Number);
        }
        public static JsonValue From(bool value)
        {
            return new JsonValue(null, value ? 1 : 0, JsonValueType.Boolean);
        }
        public static JsonValue From()
        {
            return new JsonValue(null, 0, JsonValueType.Null);
        }
    };
    public class JsonObject : IJsonValue, IEnumerable<KeyValuePair<string, IJsonValue>>
    {
        protected Dictionary<string, IJsonValue> m_map;
        T TryGetAs<T>(string index, JsonValueType type)
        {
            bool found = m_map.TryGetValue(index, out IJsonValue result);
            if (!found)
                return default;
            if (result.GetValueType() == type)
                return (T)result;
            else
                return default;
        }
        public JsonObject(Dictionary<string, IJsonValue> target)
        {
            m_map = target;
        }
        public JsonObject()
        {
            m_map = new Dictionary<string, IJsonValue>();
        }
        public JsonValueType GetValueType()
        {
            return JsonValueType.Object;
        }
        public IJsonValue this[string index]
        {
            get
            {
                return m_map[index];
            }
        }
        public int Count
        {
            get
            {
                return m_map.Count;
            }
        }
        public JsonValue GetValue(string index)
        {
            bool found = m_map.TryGetValue(index, out IJsonValue result);
            if (!found)
                return (JsonValue)null;
            JsonValueType type = result.GetValueType();
            if (type == JsonValueType.Boolean || type == JsonValueType.Number || type == JsonValueType.Null || type == JsonValueType.String)
                return (JsonValue)result;
            else
                return null;
        }
        public JsonObject GetObject(string index)
        {
            return TryGetAs<JsonObject>(index, JsonValueType.Object);
        }
        public JsonArray GetArray(string index)
        {
            return TryGetAs<JsonArray>(index, JsonValueType.Array);
        }
        public IEnumerator<KeyValuePair<string, IJsonValue>> GetEnumerator()
        {
            return m_map.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return m_map.GetEnumerator();
        }
        public void AddItem(string key, IJsonValue value)
        {
            m_map.Add(key, value);
        }
        public void RemoveItem(string key)
        {
            m_map.Remove(key);
        }
    };
    public class JsonArray : IJsonValue, IEnumerable<IJsonValue>
    {
        protected List<IJsonValue> m_list;
        public JsonArray(List<IJsonValue> target)
        {
            m_list = target;
        }
        public JsonArray()
        {
            m_list = new List<IJsonValue>();
        }
        public JsonValueType GetValueType()
        {
            return JsonValueType.Array;
        }
        public IJsonValue this[int index]
        {
            get
            {
                return m_list[index];
            }
        }
        public int Count
        {
            get
            {
                return m_list.Count;
            }
        }
        public JsonObject GetObject(int index)
        {
            IJsonValue value = m_list[index];
            return value.GetValueType() == JsonValueType.Object ? (JsonObject)value : null;
        }
        public JsonValue GetValue(int index)
        {
            IJsonValue value = m_list[index];
            return JsonValue.IsValue(value) ? (JsonValue)value : null;
        }
        public JsonArray GetArray(int index)
        {
            IJsonValue value = m_list[index];
            return value.GetValueType() == JsonValueType.Array ? (JsonArray)value : null;
        }
        public void AddItem(IJsonValue value)
        {
            m_list.Add(value);
        }
        public IEnumerator<IJsonValue> GetEnumerator()
        {
            return m_list.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return m_list.GetEnumerator();
        }
    };
}
