using System;
using System.Collections.Generic;
using System.Text;

namespace HexJson
{
    [System.Serializable]
    public class JsonParsingException : Exception
    {
        public JsonParsingException() { }
        public JsonParsingException(string message) : base(message) { }
        public JsonParsingException(string message, Exception inner) : base(message, inner) { }
        protected JsonParsingException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
    class JsonParseHelper
    {
        public static int FloatSniff(ReadOnlySpan<char> value, int index)
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
        public static char HexToChar(ReadOnlySpan<char> value, int index, int count)
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
        public static bool TryParseDouble(ReadOnlySpan<char> value, out double result)
        {
            return TryParseDouble(value, 0, value.Length, out result);
        }
        public static bool TryParseDouble(ReadOnlySpan<char> value, int index, int count, out double result)
        {
            result = default;
            bool positive = true;
            bool dot = false;
            int walk = index;
            if (value[index] == '-')
            {
                positive = false;
                walk++;
            }
            double integer = 0;
            double floater = 0;
            for (; walk < index + count; ++walk)
            {
                if (char.IsDigit(value[walk]))
                    integer = integer * 10 + (value[walk] - '0');
                else if (value[walk] == '.')
                {
                    dot = true;
                    walk++;
                    break;
                }
                else
                    return false;
            }
            if (walk == index + count && dot)
                return false;
            double dividor = 10;
            for (; walk < index + count; ++walk)
            {
                if (char.IsDigit(value[walk]))
                {
                    floater += (value[walk] - '0') / dividor;
                    dividor *= 10;
                }
                else
                    return false;
            }
            result = integer + floater;
            if (!positive)
                result = -result;
            return true;
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
    struct JsonToken
    {
        public double Value;
        public string Content;
        public JsonTokenType Type;
        public JsonToken(JsonTokenType type, string value)
        {
            Type = type;
            Content = value;
            Value = 0;
        }
        public JsonToken(JsonTokenType type, double value)
        {
            Type = type;
            Value = value;
            Content = string.Empty;
        }
    };
    ref struct JsonTokenizer
    {
        readonly ReadOnlySpan<char> m_source;
        int m_index;
        readonly int m_size;
        bool m_end;
        void SetSingleToken(ref JsonToken token, JsonTokenType type)
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
        void ReadString(ref JsonToken token)
        {
            StringBuilder builer = new StringBuilder(16);
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
                        m_index += 4;
                    }
                    else
                    {
                        char escape = char.MinValue;
                        if (!GetEscapeChar(m_source[m_index], ref escape))
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
        void ReadDigit(ref JsonToken token)
        {
            int count = JsonParseHelper.FloatSniff(m_source, m_index);
            if (count == 0)
                throw new JsonParsingException("Nought-length number is not allowed");
            double first_part = 0;
            JsonParseHelper.TryParseDouble(m_source.Slice(m_index, count), out first_part);
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
                    JsonParseHelper.TryParseDouble(m_source.Slice(m_index, sec_count), out second_part);
                    m_index += sec_count;
                    token.Value = Math.Pow(first_part, second_part);
                }
            }
            else
                token.Value = first_part;
            token.Type = JsonTokenType.Digit;
        }
        void ReadNull(ref JsonToken token)
        {
            token.Type = JsonTokenType.Null;
            if (!m_source.Slice(m_index, 4).Equals("null", StringComparison.Ordinal))
                throw new JsonParsingException("Invalid key word - null");
            token.Content = "null";
            m_index += 4;
        }
        void ReadTrue(ref JsonToken token)
        {
            token.Type = JsonTokenType.Boolean;
            token.Value = 1;
            token.Content = "true";
            if (!m_source.Slice(m_index, 4).Equals("true", StringComparison.Ordinal))
                throw new JsonParsingException("Invalid boolean value");
            m_index += 4;
        }
        void ReadFalse(ref JsonToken token)
        {
            token.Type = JsonTokenType.Boolean;
            token.Value = 0;
            token.Content = "false";
            if (!m_source.Slice(m_index, 5).Equals("false", StringComparison.Ordinal))
                throw new JsonParsingException("Invalid boolean value");
            m_index += 5;
        }
        public JsonTokenizer(string JsonString)
        {
            m_source = JsonString.AsSpan();
            m_size = JsonString.Length;
            m_index = 0;
            m_end = false;
        }
        public void Consume(ref JsonToken token)
        {
            while (char.IsWhiteSpace(m_source[m_index])) m_index++;
            char current = m_source[m_index];
            switch (current)
            {
                case '{':
                    SetSingleToken(ref token, JsonTokenType.LCurly); break;
                case '}':
                    SetSingleToken(ref token, JsonTokenType.RCurly); break;
                case '[':
                    SetSingleToken(ref token, JsonTokenType.LBracket); break;
                case ']':
                    SetSingleToken(ref token, JsonTokenType.RBracket); break;
                case ',':
                    SetSingleToken(ref token, JsonTokenType.Comma); break;
                case ':':
                    SetSingleToken(ref token, JsonTokenType.Colon); break;
                case '"':
                    ReadString(ref token); break;
                case '-':
                    ReadDigit(ref token); break;
                case 'n':
                    ReadNull(ref token); break;
                case 't':
                    ReadTrue(ref token); break;
                case 'f':
                    ReadFalse(ref token); break;
                default:
                    if (char.IsDigit(current))
                        ReadDigit(ref token);
                    break;
            }
        }
        public bool Done => m_end;
        public void Repeek(int Cnt)
        {
            m_index -= Cnt;
        }
    }
    ref struct JsonParser
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
            m_tokenizer.Consume(ref token);
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
            m_tokenizer.Consume(ref token);
            if (token.Type != JsonTokenType.LCurly)
                throw new JsonParsingException("Expected to be LCurly({)");
            while (!m_tokenizer.Done)
            {
                m_tokenizer.Consume(ref token);
                if (token.Type != JsonTokenType.String)
                {
                    if (token.Type == JsonTokenType.RCurly)
                        break;
                    throw new JsonParsingException("Expected to be String");
                }
                string key = token.Content;
                m_tokenizer.Consume(ref token);
                if (token.Type != JsonTokenType.Colon)
                    throw new JsonParsingException("Expected to be Colon(:)");
                IJsonValue value = ParseValue();
                table.Add(key, value);
                m_tokenizer.Consume(ref token);
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
            m_tokenizer.Consume(ref token);
            if (token.Type != JsonTokenType.LBracket)
                throw new JsonParsingException("Expected to be LBracket([)");
            while (!m_tokenizer.Done)
            {
                IJsonValue value = ParseValue();
                if (value == null)
                    break;
                list.Add(value);
                m_tokenizer.Consume(ref token);
                if (token.Type == JsonTokenType.RBracket)
                    break;
                if (token.Type != JsonTokenType.Comma)
                    throw new JsonParsingException("Expected to be Comma(,)");
            }
            return new JsonArray(list);
        }
    };
    /// <summary>
    /// Json Parse Service
    /// </summary>
    public class Json
    {
        /// <summary>
        /// Parse JsonObject from content
        /// </summary>
        /// <param name="Content"></param>
        /// <returns></returns>
        public static JsonObject ParseObject(string Content)
        {
            JsonParser parser = new JsonParser(Content);
            return parser.ParseObject();
        }
        /// <summary>
        /// Parse JsonArray from content
        /// </summary>
        /// <param name="Content"></param>
        /// <returns></returns>
        public static JsonArray ParseArray(string Content)
        {
            JsonParser parser = new JsonParser(Content);
            return parser.ParseArray();
        }
        /// <summary>
        /// Parse IJsonValue from content
        /// </summary>
        /// <param name="Content"></param>
        /// <returns></returns>
        public static IJsonValue Parse(string Content)
        {
            JsonParser parser = new JsonParser(Content);
            return Content.StartsWith("{") ? parser.ParseObject() as IJsonValue : parser.ParseArray() as IJsonValue;
        }
    }
}
