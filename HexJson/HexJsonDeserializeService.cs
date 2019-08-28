using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;

namespace HexJson
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class JsonDeserializationAttribute : Attribute { };

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class JsonFieldAttribute : Attribute
    {
        public JsonFieldAttribute(string Field, string Pipe = null, string InversePipe = null)
        {
            JsonField = Field;
            DeserializationPipe = Pipe;
            SerializationPipe = InversePipe;
        }
        /// <summary>
        /// Target json field key
        /// </summary>
        public string JsonField { get; set; }
        /// <summary>
        /// Custom deserialization pipe name
        /// </summary>
        public string DeserializationPipe { get; set; }
        /// <summary>
        /// Custom serialization pipe name
        /// </summary>
        public string SerializationPipe { get; set; }
        /// <summary>
        /// Custom string encoding
        /// </summary>
        public Encoding StringEncoding { get; set; }
    };
    enum FieldType
    {
        Property,
        Field,
        List,
        ListWithNest
    }
    class JsonFieldInfo
    {
        public FieldType TargetFieldType;
        public PropertyInfo Property;
        public Type FieldType;
        public FieldInfo Field;
        public Type NestedType;
        public string JsonKey;
        public Func<IJsonValue, object> Pipe;
        public Func<object, string> InversePipe;
        public JsonFieldInfo[] JsonFields;
        public void SetValue(object target, object value)
        {
            if (Field != null)
                Field.SetValue(target, value);
            else if (Property != null)
                Property.SetValue(target, value);
            else
                throw new JsonRuntimeException("Invalid meta data");
        }
        public object GetValue(object target)
        {
            if (Field != null)
                return Field.GetValue(target);
            else if (Property != null)
                return Property.GetValue(target);
            else
                throw new JsonRuntimeException("Invalid meta data");
        }
    }
    class JsonDeserializationMetaData
    {
        public Type ObjectType;
        public JsonFieldInfo[] JsonFields;
    }
    /// <summary>
    /// Json deserialization service
    /// </summary>
    public class JsonDeserialization
    {
        private static ConcurrentDictionary<Type, JsonDeserializationMetaData> m_meta_cache = new ConcurrentDictionary<Type, JsonDeserializationMetaData>();
        private static JsonFieldInfo[] Parse(Type ObjectType)
        {
            var JsonFields = new List<JsonFieldInfo>();
            if (ObjectType == null || ObjectType.GetCustomAttribute(typeof(JsonDeserializationAttribute)) == null)
                return null;
            foreach (var member in ObjectType.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
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
                JsonFieldInfo setter = new JsonFieldInfo();
                setter.JsonKey = attribute.JsonField;
                if (attribute.DeserializationPipe != null)
                {
                    var method = ObjectType.GetMethod(attribute.DeserializationPipe, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                    if (method == null)
                        throw new JsonRuntimeException("Pipe method does not exist!");
                    setter.Pipe = method.CreateDelegate(typeof(Func<IJsonValue, object>)) as Func<IJsonValue, object>;
                }
                if (member.MemberType == MemberTypes.Property)
                {
                    setter.TargetFieldType = FieldType.Property;
                    var property_info = member as PropertyInfo;
                    if (!property_info.CanWrite)
                        continue;
                    setter.Property = property_info;
                    setter.FieldType = property_info.PropertyType;
                }
                else if (member.MemberType == MemberTypes.Field)
                {
                    setter.TargetFieldType = FieldType.Field;
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
                            setter.TargetFieldType = FieldType.ListWithNest;
                            setter.JsonFields = Parse(setter.NestedType);
                        }
                        else
                            setter.TargetFieldType = FieldType.List;
                    }
                    else if (setter.FieldType.IsConstructedGenericType)//动态数组
                    {
                        if (typeof(IList).IsAssignableFrom(setter.FieldType))
                        {
                            setter.NestedType = setter.FieldType.GetGenericArguments()[0];
                            if (setter.NestedType != typeof(string) && setter.NestedType != typeof(object) && !setter.NestedType.IsPrimitive)
                            {
                                setter.TargetFieldType = FieldType.ListWithNest;
                                setter.JsonFields = Parse(setter.NestedType);
                            }
                            else
                                setter.TargetFieldType = FieldType.List;
                        }
                        else
                            continue;
                    }
                    else
                        setter.JsonFields = Parse(setter.FieldType);
                }
                JsonFields.Add(setter);
            }
            return JsonFields.ToArray();
        }
        private static object DeserializeObject(Type TargetType, JsonObject JsonTarget, JsonFieldInfo[] JsonFields)
        {
            object ret = Activator.CreateInstance(TargetType);
            foreach (var setter in JsonFields)
            {
                //自定义管道
                if (setter.Pipe != null)
                {
                    object value = setter.Pipe(JsonTarget[setter.JsonKey]);
                    setter.SetValue(ret, value);
                    continue;
                }
                switch (setter.TargetFieldType)
                {
                    case FieldType.Field:
                        setter.Field.SetValue(ret, Convert.ChangeType(JsonTarget.GetValue(setter.JsonKey).GetValue(), setter.FieldType));
                        break;
                    case FieldType.Property:
                        setter.Property.SetValue(ret, Convert.ChangeType(JsonTarget.GetValue(setter.JsonKey).GetValue(), setter.FieldType));
                        break;
                    case FieldType.List:
                        {
                            JsonArray json_array = JsonTarget.GetArray(setter.JsonKey);
                            object value = null;
                            if (setter.FieldType.IsArray)
                            {
                                Array array = Activator.CreateInstance(setter.FieldType, json_array.Count) as Array;
                                value = array;
                                for (int i = 0; i < array.Length; ++i)
                                    array.SetValue(Convert.ChangeType(json_array.GetValue(i).GetValue(), setter.NestedType), i);
                            }
                            else
                            {
                                IList list = Activator.CreateInstance(setter.FieldType) as IList;
                                value = list;
                                foreach (var item in json_array)
                                    list.Add(Convert.ChangeType((item as JsonValue).GetValue(), setter.NestedType));
                            }
                            if (setter.Field != null)
                                setter.Field.SetValue(ret, value);
                            else
                                setter.Property.SetValue(ret, value);
                            break;
                        }
                    case FieldType.ListWithNest:
                        {
                            JsonArray json_array = JsonTarget.GetArray(setter.JsonKey);
                            object value = null;
                            if (setter.FieldType.IsArray)
                            {
                                Array array = Activator.CreateInstance(setter.FieldType, json_array.Count) as Array;
                                value = array;
                                for (int i = 0; i < array.Length; ++i)
                                    array.SetValue(DeserializeObject(setter.NestedType, json_array.GetObject(i), setter.JsonFields), i);
                            }
                            else
                            {
                                IList list = Activator.CreateInstance(setter.FieldType) as IList;
                                value = list;
                                foreach (var item in json_array)
                                    list.Add(DeserializeObject(setter.NestedType, item as JsonObject, setter.JsonFields));
                            }
                            if (setter.Field != null)
                                setter.Field.SetValue(ret, value);
                            else
                                setter.Property.SetValue(ret, value);
                        }
                        break;
                }
            }
            return ret;
        }
        private static JsonDeserializationMetaData GetMetaData(Type TargetType)
        {
            if (m_meta_cache.TryGetValue(TargetType, out var value))
                return value;
            var generated = new JsonDeserializationMetaData() { ObjectType = TargetType, JsonFields = Parse(TargetType) };
            m_meta_cache.TryAdd(TargetType, generated);
            return generated;
        }
        private static object Deserialize(JsonDeserializationMetaData MetaData, JsonObject JsonTarget)
        {
            return DeserializeObject(MetaData.ObjectType, JsonTarget, MetaData.JsonFields);
        }
        /// <summary>
        /// Deserialize Object
        /// </summary>
        /// <param name="TargetType">Target Type</param>
        /// <param name="JsonTarget">JsonObject</param>
        /// <returns>Deserialized Object</returns>
        public static object Deserialize(Type TargetType, JsonObject JsonTarget)
        {
            var meta = GetMetaData(TargetType);
            return DeserializeObject(TargetType, JsonTarget, meta.JsonFields);
        }
        /// <summary>
        /// Deserialize JsonArray
        /// </summary>
        /// <param name="TargetType">ElementType</param>
        /// <param name="JsonTarget">JsonArray</param>
        /// <returns>TargetType[]</returns>
        public static object Deserialize(Type TargetType, JsonArray JsonTarget)
        {
            var meta = GetMetaData(TargetType);
            return (from item in JsonTarget select Deserialize(TargetType, item as JsonObject)).ToArray();
        }
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="JsonTarget"></param>
        /// <returns></returns>
        public static T[] Deserialize<T>(JsonArray JsonTarget) where T : class
        {
            var meta = GetMetaData(typeof(T));
            return (from item in JsonTarget select Deserialize<T>(item as JsonObject)).ToArray();
        }
        /// <summary>
        /// Deserialize Object
        /// </summary>
        /// <typeparam name="T">Target Type</typeparam>
        /// <param name="JsonTarget">JsonObject</param>
        /// <returns>Deserialized Object</returns>
        public static T Deserialize<T>(JsonObject JsonTarget) where T : class
        {
            var meta = GetMetaData(typeof(T));
            return DeserializeObject(typeof(T), JsonTarget, meta.JsonFields) as T;
        }
    }
    /// <summary>
    /// Json serialization service
    /// </summary>
    public class JsonSerialization
    {

    }
}
