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
        public JsonFieldAttribute(string Field, string Pipe = null)
        {
            JsonField = Field;
            PipeKey = Pipe;
        }
        /// <summary>
        /// Target Field Key
        /// </summary>
        public string JsonField { get; set; }
        /// <summary>
        /// Custom Pipe Name
        /// </summary>
        public string PipeKey { get; set; }
    };
    enum FieldType
    {
        Property,
        Field,
        List,
        ListWithNest
    }
    class FieldSetter
    {
        public FieldType SetterFieldType;
        public PropertyInfo Property;
        public Type FieldType;
        public FieldInfo Field;
        public Type NestedType;
        public string JsonKey;
        public Func<IJsonValue, object> Pipe;
        public FieldSetter[] ChildSetters;
    }
    class JsonDeserializationMetaData
    {
        public Type ObjectType;
        public FieldSetter[] Setters;
    }
    /// <summary>
    /// JsonDeserialization Service
    /// </summary>
    public class JsonDeserialization
    {
        private static ConcurrentDictionary<Type, JsonDeserializationMetaData> m_meta_cache = new ConcurrentDictionary<Type, JsonDeserializationMetaData>();
        private static FieldSetter[] ParseObject(Type ObjectType)
        {
            var setters = new List<FieldSetter>();
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
                FieldSetter setter = new FieldSetter();
                setter.JsonKey = attribute.JsonField;
                if (attribute.PipeKey != null)
                {
                    var method = ObjectType.GetMethod(attribute.PipeKey, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                    if (method == null)
                        throw new JsonRuntimeException("Pipe method does not exist!");
                    setter.Pipe = method.CreateDelegate(typeof(Func<IJsonValue, object>)) as Func<IJsonValue, object>;
                }
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
        private static object DeserializeObject(Type TargetType, JsonObject JsonTarget, FieldSetter[] Setters)
        {
            object ret = Activator.CreateInstance(TargetType);
            foreach (var setter in Setters)
            {
                //自定义管道
                if (setter.Pipe != null)
                {
                    object value = setter.Pipe(JsonTarget[setter.JsonKey]);
                    switch (setter.SetterFieldType)
                    {
                        case FieldType.Field:
                            setter.Field.SetValue(ret, value);
                            break;
                        case FieldType.Property:
                            setter.Property.SetValue(ret, value);
                            break;
                        case FieldType.List:
                        case FieldType.ListWithNest:
                            if (setter.Field != null)
                                setter.Field.SetValue(ret, value);
                            else
                                setter.Property.SetValue(ret, value);
                            break;
                    }
                    continue;
                }
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
                                    array.SetValue(DeserializeObject(setter.NestedType, json_array.GetObject(i), setter.ChildSetters), i);
                            }
                            else
                            {
                                IList list = Activator.CreateInstance(setter.FieldType) as IList;
                                value = list;
                                foreach (var item in json_array)
                                    list.Add(DeserializeObject(setter.NestedType, item as JsonObject, setter.ChildSetters));
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
            var generated = new JsonDeserializationMetaData() { ObjectType = TargetType, Setters = ParseObject(TargetType) };
            m_meta_cache.TryAdd(TargetType, generated);
            return generated;
        }
        private static object Deserialize(JsonDeserializationMetaData MetaData, JsonObject JsonTarget)
        {
            return DeserializeObject(MetaData.ObjectType, JsonTarget, MetaData.Setters);
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
            return DeserializeObject(TargetType, JsonTarget, meta.Setters);
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
            return DeserializeObject(typeof(T), JsonTarget, meta.Setters) as T;
        }
    }
}
