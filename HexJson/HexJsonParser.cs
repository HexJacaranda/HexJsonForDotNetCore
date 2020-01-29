using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace HexJson
{
    [Serializable]
    public class JsonParsingException : Exception
    {
        public JsonParsingException() { }
        public JsonParsingException(string message) : base(message) { }
        public JsonParsingException(string message, Exception inner) : base(message, inner) { }
        protected JsonParsingException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
    static class JsonParseHelper
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
        public static char HexToChar(char hex)
        {
            if (hex >= 'a' && hex <= 'f')
                return (char)(hex - 'a');
            if (hex >= 'A' && hex <= 'F')
                return (char)(hex - 'A');
            if (hex >= '0' && hex <= '9')
                return (char)(hex - '0');
            return default;
        }
        public static char HexToChars(ReadOnlySpan<char> value)
        {
            char ret = default;
            for (int i = 3; i >= 0; --i)
                ret |= (char)(HexToChar(value[3 - i]) << i * 4);
            return ret;
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
    ref struct JsonLexer
    {
        readonly ReadOnlySpan<char> m_source;
        int m_index;
        void SetSingleToken(ref JsonToken token, JsonTokenType type)
        {
            token.Type = type;
            token.Value = m_source[m_index++];
            if (m_index == m_source.Length)
                return;
        }
        static bool GetEscapeChar(char wc, out char corresponding) => (corresponding = wc switch
        {
            'n' => '\n',
            'b' => '\b',
            'r' => '\r',
            't' => '\t',
            'f' => '\f',
            '"' => '"',
            '\\' => '\\',
            '/' => '/',
            'u' => 'u',
            _ => default
        }) != default;

        void ReadString(ref JsonToken token)
        {
            StringBuilder builer = new StringBuilder(16);
            token.Type = JsonTokenType.String;
            m_index++;
            while(true)
            {
                if (m_source[m_index] == '\\')//转义
                {
                    m_index++;
                    if (m_source[m_index] == 'u')//Unicode转义
                    {
                        m_index++;
                        char unicode = JsonParseHelper.HexToChars(m_source.Slice(m_index, 4));
                        builer.Append(unicode);
                        m_index += 4;
                    }
                    else
                    {
                        if (!GetEscapeChar(m_source[m_index],out var escape))
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
            if (!JsonParseHelper.TryParseDouble(m_source.Slice(m_index, count), out var first_part))
                throw new JsonParsingException("Malformed number");
            m_index += count;
            if (m_source[m_index] == 'E' || m_source[m_index] == 'e')
            {
                m_index++;
                int sec_count = JsonParseHelper.FloatSniff(m_source, m_index);
                if (sec_count == 0)
                    throw new JsonParsingException("Nought-length exponent is not allowed");
                else
                {
                    if (!JsonParseHelper.TryParseDouble(m_source.Slice(m_index, sec_count), out var second_part))
                        throw new JsonParsingException("Malformed exponent");
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
        public JsonLexer(string JsonString)
        {
            m_source = JsonString.AsSpan();
            m_index = 0;
        }
        public JsonLexer(ReadOnlySpan<char> Json)
        {
            m_source = Json;
            m_index = 0;
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
        public bool Done => m_index == m_source.Length;
        public void Repeek(int Cnt)
        {
            m_index -= Cnt;
        }
    }
    ref struct JsonParser
    {
        JsonLexer m_tokenizer;
        public JsonParser(string target)
        {
            m_tokenizer = new JsonLexer(target);
        }
        public JsonParser(ReadOnlySpan<char> target)
        {
            m_tokenizer = new JsonLexer(target);
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
    /// Json Service
    /// </summary>
    public static partial class Json
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
        /// <summary>
        /// Parse IJsonValue from any stream with custom encoding
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="encoding"></param>
        /// <returns></returns>
        public static IJsonValue Parse(Stream stream, Encoding encoding)
        {
            var sequence = new StreamReader(stream, encoding).ReadToEnd();
            return Parse(sequence);
        }
        /// <summary>
        /// Parse IJsonValue from any stream with UTF-8
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="encoding"></param>
        /// <returns></returns>
        public static IJsonValue Parse(Stream stream)
        {
            var sequence = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
            return Parse(sequence);
        }
    }
}
