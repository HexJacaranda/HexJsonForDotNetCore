using System;
using System.Collections.Generic;
using System.Text;

namespace HexJson
{
    public class JsonFormatter
    {
        private StringBuilder m_text = new StringBuilder();
        private const int ASCII = 127;
        private void Text(string Target)
        {
            m_text.Append(Target);
        }
        private void Text(Span<char> Target)
        {
            m_text.Append(Target);
        }
        private void Text(char Target)
        {
            m_text.Append(Target);
        }
        private void TextViaUnicode(string Target)
        {
            if (TextEncoding)
            {
                for (int i = 0; i < Target.Length; ++i)
                {
                    if (Target[i] > ASCII)
                    {
                        Text('\\');
                        Text('u');
                        Span<char> buffer = new char[4];
                        ToUnicodeFormat(Target[i], buffer);
                        Text(buffer);
                    }
                    else
                        Text(Target[i]);
                }
            }
            else
                Text(Target);
        }
        private static void ToUnicodeFormat(char Target, Span<char> Buffer)
        {
            int div = Target;
            for (int i = 0; i < Buffer.Length; ++i)
            {
                int remain = div % 16;
                div >>= 4;
                Buffer[Buffer.Length - 1 - i] = (char)(remain > 9 ? remain - 10 + 'a' : remain + '0');
            }
        }
        private void Roll(int Cnt)
        {
            m_text.Remove(m_text.Length - 1, Cnt);
        }
        private void JsonValueFormat(IJsonValue Target)
        {
            if (JsonValue.IsValue(Target))
                ValueFormat(Target as JsonValue);
            else if (Target.GetValueType() == JsonValueType.Array)
                ArrayFormat(Target as JsonArray);
            else if (Target.GetValueType() == JsonValueType.Object)
                ObjectFormat(Target as JsonObject);
            else
                throw new JsonRuntimeException("Unexpected IJsonValue.Type");
        }
        private void ObjectFormat(JsonObject Target)
        {
            Text('{');
            foreach (var item in Target)
            {
                Text('"');
                Text(item.Key);
                Text('"');
                Text(':');
                JsonValueFormat(item.Value);
                Text(',');
            }
            if (Target.Count > 0)
                Roll(1);
            Text('}');
        }
        private void ArrayFormat(JsonArray Target)
        {
            Text('[');
            foreach (var item in Target)
            {
                JsonValueFormat(item);
                Text(',');
            }
            if (Target.Count > 0)
                Roll(1);
            Text(']');
        }
        private void ValueFormat(JsonValue Target)
        {
            switch (Target.GetValueType())
            {
                case JsonValueType.Boolean:
                    if (Target.AsBoolean()) Text("true"); else Text("false");
                    break;
                case JsonValueType.Null:
                    Text("null");
                    break;
                case JsonValueType.Number:
                    Text(Target.AsDouble().ToString());
                    break;
                case JsonValueType.String:
                    Text('"');
                    TextViaUnicode(Target.AsString());
                    Text('"');
                    break;
                default:
                    throw new JsonRuntimeException("Invalid type for formatter");
            }
        }
        public bool TextEncoding { get; set; }
        public string Format
        {
            get
            {
                return m_text.ToString();
            }
        }
        public static string JsonFormat(IJsonValue target, bool encoding = true)
        {
            JsonFormatter formatter = new JsonFormatter() { TextEncoding = encoding };
            formatter.JsonValueFormat(target);
            return formatter.Format;
        }
    }
}
