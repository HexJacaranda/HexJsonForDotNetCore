using System;
using System.Text;
using System.Reflection;
using System.Collections;
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
    //Json值
    public class JsonValue : IJsonValue
    {
        protected string m_string;
        protected double m_cache;
        protected JsonValueType m_type;
        public JsonValueType GetValueType()
        {
            return m_type;
        }
        public double AsDouble()
        {
            if (m_type == JsonValueType.Number)
                return m_cache;
            throw new Exception("Not Float");
        }
        public string AsString()
        {
            if (m_type == JsonValueType.String)
                return m_string;
            throw new Exception("Error type");
        }
        public int AsInt()
        {
            return (int)m_cache;
        }
        public bool AsBoolean()
        {
            if (m_type == JsonValueType.Boolean)
                return m_cache == 1;
            throw new Exception("Error type");
        }
        public object GetValue()
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
        public JsonValue(string value, double cache, JsonValueType type)
        {
            m_cache = cache;
            m_string = value;
            m_type = type;
        }
    };
    //Json对象
    public class JsonObject : IJsonValue, IEnumerable<KeyValuePair<string, IJsonValue>>
    {
        protected Dictionary<string, IJsonValue> m_map;
        T TryGetAs<T>(string index, JsonValueType type)
        {
            bool found = m_map.TryGetValue(index, out IJsonValue result);
            if (!found)
                return default(T);
            if (result.GetValueType() == type)
                return (T)result;
            else
                return default(T);
        }
        public JsonValueType GetValueType()
        {
            return JsonValueType.Object;
        }
        //取出值
        public IJsonValue this[string index]
        {
            get
            {
                return m_map[index];
            }
        }
        //数目
        public int Count
        {
            get
            {
                return m_map.Count;
            }
        }
        //取出值,作为Value
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
        //取出值,作为Object
        public JsonObject GetObject(string index)
        {
            return TryGetAs<JsonObject>(index, JsonValueType.Object);
        }
        //取出值,作为Array
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
        public JsonObject(Dictionary<string, IJsonValue> target)
        {
            m_map = target;
        }
    };
    //Json数组
    public class JsonArray : IJsonValue, IEnumerable<IJsonValue>
    {
        protected List<IJsonValue> m_list;

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
        //数目
        public int Count
        {
            get
            {
                return m_list.Count;
            }
        }
        //取出值,作为Object
        public JsonObject GetObject(int index)
        {
            IJsonValue value = m_list[index];
            return value.GetValueType() == JsonValueType.Object ? (JsonObject)value : null;
        }
        //取出值,作为Value
        public JsonValue GetValue(int index)
        {
            IJsonValue value = m_list[index];
            return JsonValue.IsValue(value) ? (JsonValue)value : null;
        }
        //取出值,作为Array
        public JsonArray GetArray(int index)
        {
            IJsonValue value = m_list[index];
            return value.GetValueType() == JsonValueType.Array ? (JsonArray)value : null;
        }
        public IEnumerator<IJsonValue> GetEnumerator()
        {
            return m_list.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return m_list.GetEnumerator();
        }
        public JsonArray(List<IJsonValue> target)
        {
            m_list = target;
        }
    };
    //Json解析异常
    public class JsonParsingException : Exception
    {
        public JsonParsingException(string msg) : base(msg)
        {

        }
    };
    class JsonParseHelper
    {
        public static int FloatSniff(string value, int index)
        {
            int ret = 0;
            short dot_times = 0;
            if (value[index] == '-' || char.IsDigit(value[index]))
            {
                index++;
                ret++;
                while (true)
                {
                    if (value[index] == '.')
                    {
                        ++dot_times;
                        if (dot_times > 1)
                            return 0;
                    }
                    else if (!char.IsDigit(value[index]))
                        break;
                    ++index;
                    ++ret;
                }
            }
            return ret;
        }
        public static bool IsHex(char c)
        {
            return c >= 'a' && c <= 'f';
        }
        public static char HexToChar(string value, int index, int count)
        {
            int ret = 0;
            int factor = 0;
            for (int i = count - 1; i >= 0; --i)
            {
                if (IsHex(value[index]))
                    factor = (value[index] - 'a') + 10;
                else if (char.IsDigit(value[index]))
                    factor = value[index] - '0';
                else
                    return (char)ret;
                ret += factor * (int)Math.Pow(16, i);
                index++;
            }
            return (char)ret;
        }
    }
    enum JsonTokenType
    {
        String,
        Digit,
        Null,
        Boolean,
        LBracket,
        RBracket,
        LCurly,
        RCurly,
        Comma,
        Colon
    };
    class JsonToken
    {
        public double Value = 0;
        public string Content = string.Empty;
        public JsonTokenType Type = JsonTokenType.Null;
        public JsonToken(JsonTokenType type, string value)
        {
            Type = type;
            Content = value;
        }
        public JsonToken(JsonTokenType type, double value)
        {
            Type = type;
            Value = value;
        }
        public JsonToken() { }
    };
    class JsonTokenizer
    {
        readonly string m_source = null;
        int m_index = 0;
        readonly int m_size = 0;
        bool m_end = false;
        void SetSingleToken(JsonToken token, JsonTokenType type)
        {
            token.Type = type;
            token.Value = m_source[m_index];
            if (m_index == m_size - 1)
            {
                m_end = true;
                return;
            }
            m_index++;
        }
        static bool GetEscapeChar(char wc, ref char corresponding)
        {
            if (wc == 'n')
                corresponding = '\n';
            else if (wc == 'b')
                corresponding = '\b';
            else if (wc == 'r')
                corresponding = '\r';
            else if (wc == 't')
                corresponding = '\t';
            else if (wc == 'f')
                corresponding = '\f';
            else if (wc == '"')
                corresponding = '"';
            else if (wc == '\\')
                corresponding = '\\';
            else if (wc == '/')
                corresponding = '/';
            else if (wc == 'u')
                corresponding = 'u';
            else
                return false;
            return true;
        }
        void ReadString(JsonToken token)
        {
            StringBuilder builer = new StringBuilder();
            token.Type = JsonTokenType.String;
            m_index++;
            for (; ; )
            {
                if (m_source[m_index] == '\\')//转义
                {
                    m_index++;
                    if (m_source[m_index] == 'u')//Unicode转义
                    {
                        m_index++;
                        char unicode = JsonParseHelper.HexToChar(m_source, m_index, 4);
                        builer.Append(unicode);
                    }
                    else
                    {
                        char escape = char.MinValue;
                        if (GetEscapeChar(m_source[m_index], ref escape))
                            throw new JsonParsingException("Invalid escape character");
                        builer.Append(escape);
                        m_index++;
                    }
                }
                else if (m_source[m_index] == '"')
                {
                    token.Content = builer.ToString();
                    m_index++;
                    return;
                }
                else
                    builer.Append(m_source[m_index++]);
            }
        }
        void ReadDigit(JsonToken token)
        {
            int count = JsonParseHelper.FloatSniff(m_source, m_index);
            if (count == 0)
                throw new JsonParsingException("Nought-length number is not allowed");
            double first_part = 0;
            double.TryParse(m_source.Substring(m_index, count), out first_part);
            m_index += count;
            if (m_source[m_index] == 'E' || m_source[m_index] == 'e')
            {
                m_index++;
                int sec_count = JsonParseHelper.FloatSniff(m_source, m_index);
                if (sec_count == 0)
                    throw new JsonParsingException("Nought-length exponent is not allowed");
                else
                {
                    double second_part = 0;
                    double.TryParse(m_source.Substring(m_index, sec_count), out second_part);
                    m_index += sec_count;
                    token.Value = Math.Pow(first_part, second_part);
                }
            }
            else
                token.Value = first_part;
            token.Type = JsonTokenType.Digit;
        }
        void ReadNull(JsonToken token)
        {
            token.Type = JsonTokenType.Null;
            if (m_source.Substring(m_index, 4) != "null")
                throw new JsonParsingException("Invalid key word - null");
            token.Content = "null";
            m_index += 4;
        }
        void ReadTrue(JsonToken token)
        {
            token.Type = JsonTokenType.Boolean;
            token.Value = 1;
            token.Content = "true";
            if (m_source.Substring(m_index, 4) != "true")
                throw new JsonParsingException("Invalid boolean value");
            m_index += 4;
        }
        void ReadFalse(JsonToken token)
        {
            token.Type = JsonTokenType.Boolean;
            token.Value = 0;
            token.Content = "false";
            if (m_source.Substring(m_index, 5) != "false")
                throw new JsonParsingException("Invalid boolean value");
            m_index += 5;
        }
        public JsonTokenizer(string JsonString)
        {
            m_source = JsonString;
            m_size = JsonString.Length;
        }
        public void Consume(JsonToken token)
        {
            while (char.IsWhiteSpace(m_source[m_index])) m_index++;
            switch (m_source[m_index])
            {
                case '{':
                    SetSingleToken(token, JsonTokenType.LCurly); break;
                case '}':
                    SetSingleToken(token, JsonTokenType.RCurly); break;
                case '[':
                    SetSingleToken(token, JsonTokenType.LBracket); break;
                case ']':
                    SetSingleToken(token, JsonTokenType.RBracket); break;
                case ',':
                    SetSingleToken(token, JsonTokenType.Comma); break;
                case ':':
                    SetSingleToken(token, JsonTokenType.Colon); break;
                case '"':
                    ReadString(token); break;
                case '-':
                    ReadDigit(token); break;
                case 'n':
                    ReadNull(token); break;
                case 't':
                    ReadTrue(token); break;
                case 'f':
                    ReadFalse(token); break;
                default:
                    break;
            }
            if (char.IsDigit(m_source[m_index]))
                ReadDigit(token);
        }
        public bool Done => m_end;
        public void Repeek(int Cnt)
        {
            m_index -= Cnt;
        }
    }
    //Json解析器
    public class JsonParser
    {
        JsonTokenizer m_tokenizer;
        public JsonParser(string target)
        {
            m_tokenizer = new JsonTokenizer(target);
        }
        IJsonValue ParseValue()
        {
            JsonToken token = new JsonToken();
            if (m_tokenizer.Done)
                return null;
            m_tokenizer.Consume(token);
            switch (token.Type)
            {
                case JsonTokenType.LCurly:
                    m_tokenizer.Repeek(1);
                    return ParseObject();
                case JsonTokenType.LBracket:
                    m_tokenizer.Repeek(1);
                    return ParseArray();
                case JsonTokenType.String:
                    return new JsonValue(token.Content, token.Value, JsonValueType.String);
                case JsonTokenType.Digit:
                    return new JsonValue(token.Content, token.Value, JsonValueType.Number);
                case JsonTokenType.Boolean:
                    return new JsonValue(token.Content, token.Value, JsonValueType.Boolean);
                case JsonTokenType.Null:
                    return new JsonValue(token.Content, token.Value, JsonValueType.Null);
            }
            return null;
        }
        public JsonObject ParseObject()
        {
            Dictionary<string, IJsonValue> table = new Dictionary<string, IJsonValue>();
            JsonToken token = new JsonToken();
            m_tokenizer.Consume(token);
            if (token.Type != JsonTokenType.LCurly)
                throw new JsonParsingException("Expected to be LCurly({)");
            while (!m_tokenizer.Done)
            {
                m_tokenizer.Consume(token);
                if (token.Type != JsonTokenType.String)
                {
                    if (token.Type == JsonTokenType.RCurly)
                        break;
                    throw new JsonParsingException("Expected to be String");
                }
                string key = token.Content;
                m_tokenizer.Consume(token);
                if (token.Type != JsonTokenType.Colon)
                    throw new JsonParsingException("Expected to be Colon(:)");
                IJsonValue value = ParseValue();
                table.Add(key, value);
                m_tokenizer.Consume(token);
                if (token.Type == JsonTokenType.RCurly)
                    break;
                if (token.Type != JsonTokenType.Comma)
                    throw new JsonParsingException("Expected to be Comma(,)");
            }
            return new JsonObject(table);
        }
        public JsonArray ParseArray()
        {
            List<IJsonValue> list = new List<IJsonValue>();
            JsonToken token = new JsonToken();
            m_tokenizer.Consume(token);
            if (token.Type != JsonTokenType.LBracket)
                throw new JsonParsingException("Expected to be LBracket([)");
            while (!m_tokenizer.Done)
            {
                IJsonValue value = ParseValue();
                if (value == null)
                    break;
                list.Add(value);
                m_tokenizer.Consume(token);
                if (token.Type == JsonTokenType.RBracket)
                    break;
                if (token.Type != JsonTokenType.Comma)
                    throw new JsonParsingException("Expected to be Comma(,)");
            }
            return new JsonArray(list);
        }
    };
    public class Json
    {
        public static JsonObject ParseObject(string Content)
        {
            JsonParser parser = new JsonParser(Content);
            return parser.ParseObject();
        }
        public static JsonArray ParseArray(string Content)
        {
            JsonParser parser = new JsonParser(Content);
            return parser.ParseArray();
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class JsonDeserializationAttribute : Attribute { };

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class JsonFieldAttribute : Attribute
    {
        public string JsonField { get; set; }
	};
    public enum FieldType
    {
        Property,
        Field,
        List,
        ListWithNest
    }
    public class FieldSetter
    {
        public FieldType SetterFieldType;
        public PropertyInfo Property;
        public Type FieldType;
        public FieldInfo Field;
        public Type NestedType;
        public string JsonKey;
        public FieldSetter[] ChildSetters;
    }
    public class JsonDeserializationMetaData
    {
        public Type ObjectType;
        public FieldSetter[] Setters;
    }
    public class JsonDeserialization
    {
        private static FieldSetter[] ParseObject(Type ObjectType)
        {
            var setters = new List<FieldSetter>();
            if (ObjectType == null || ObjectType.GetCustomAttribute(typeof(JsonDeserializationAttribute)) == null)
                return null;
            foreach (var member in ObjectType.GetMembers())
            {
                JsonFieldAttribute attribute = null;
                if (member.MemberType == MemberTypes.Property || member.MemberType == MemberTypes.Field)
                {
                    attribute = member.GetCustomAttribute(typeof(JsonFieldAttribute)) as JsonFieldAttribute;
                    if (attribute == null)
                        continue;
                }
                else
                    continue;
                FieldSetter setter = new FieldSetter();
                setter.JsonKey = attribute.JsonField;
                if (member.MemberType == MemberTypes.Property)
                {
                    setter.SetterFieldType = FieldType.Property;
                    var property_info = member as PropertyInfo;
                    if (!property_info.CanWrite)
                        continue;
                    setter.Property = property_info;
                    setter.FieldType = property_info.PropertyType;
                }
                else if (member.MemberType == MemberTypes.Field)
                {
                    setter.SetterFieldType = FieldType.Field;
                    setter.Field = member as FieldInfo;
                    setter.FieldType = setter.Field.FieldType;
                }
                if (setter.FieldType.IsClass && setter.FieldType != typeof(string) && setter.FieldType != typeof(object))//非基本类型
                {
                    if (setter.FieldType.IsArray)//数组
                    {
                        setter.NestedType = setter.FieldType.GetElementType();
                        if (setter.NestedType != typeof(string) && setter.NestedType != typeof(object) && !setter.NestedType.IsPrimitive)                      
                        {
                            setter.SetterFieldType = FieldType.ListWithNest;
                            setter.ChildSetters = ParseObject(setter.NestedType);
                        }
                        else
                            setter.SetterFieldType = FieldType.List;
                    }
                    else if (setter.FieldType.IsConstructedGenericType)//动态数组
                    {
                        if (typeof(IList).IsAssignableFrom(setter.FieldType))
                        {                      
                            setter.NestedType = setter.FieldType.GetGenericArguments()[0];                        
                            if (setter.NestedType != typeof(string) && setter.NestedType != typeof(object) && !setter.NestedType.IsPrimitive)
                            {
                                setter.SetterFieldType = FieldType.ListWithNest;
                                setter.ChildSetters = ParseObject(setter.NestedType);
                            }
                            else
                                setter.SetterFieldType = FieldType.List;
                        }
                        else
                            continue;
                    }
                    else
                        setter.ChildSetters = ParseObject(setter.FieldType);
                }
                setters.Add(setter);
            }
            return setters.ToArray();
        }
        private static object DeserializeObject(Type TargetType,JsonObject JsonTarget, FieldSetter[] Setters)
        {
            object ret = Activator.CreateInstance(TargetType);
            foreach (var setter in Setters)
            {
                switch (setter.SetterFieldType)
                {
                    case FieldType.Field:
                        setter.Field.SetValue(ret, Convert.ChangeType(JsonTarget.GetValue(setter.JsonKey).GetValue(), setter.FieldType));
                        break;
                    case FieldType.Property:
                        setter.Property.SetValue(ret, Convert.ChangeType(JsonTarget.GetValue(setter.JsonKey).GetValue(), setter.FieldType));
                        break;                      
                    case FieldType.List:
                        {
                            IList list = Activator.CreateInstance(setter.FieldType) as IList;
                            JsonArray array = JsonTarget.GetArray(setter.JsonKey);
                            foreach (var item in array)
                                list.Add(Convert.ChangeType((item as JsonValue).GetValue(), setter.NestedType));
                            if (setter.Field != null)
                                setter.Field.SetValue(ret, list);
                            else
                                setter.Property.SetValue(ret, list);
                            break;
                        }
                    case FieldType.ListWithNest:
                        {
                            IList list = Activator.CreateInstance(setter.FieldType) as IList;
                            JsonArray array = JsonTarget.GetArray(setter.JsonKey);
                            foreach (var item in array)
                                list.Add(DeserializeObject(setter.NestedType, item as JsonObject, setter.ChildSetters));
                            if (setter.Field != null)
                                setter.Field.SetValue(ret, list);
                            else
                                setter.Property.SetValue(ret, list);
                        }
                        break;               
                }
            }
            return ret;
        }
        public static JsonDeserializationMetaData GetMetaData<T>()
        {
            return new JsonDeserializationMetaData() { ObjectType = typeof(T), Setters = ParseObject(typeof(T)) };
        }
        public static JsonDeserializationMetaData GetMetaData(Type TargetType)
        {
            return new JsonDeserializationMetaData() { ObjectType = TargetType, Setters = ParseObject(TargetType) };
        }
        public static object Deserialize(JsonDeserializationMetaData MetaData,JsonObject JsonTarget)
        {
            return DeserializeObject(MetaData.ObjectType, JsonTarget, MetaData.Setters);
        }
    }
}
