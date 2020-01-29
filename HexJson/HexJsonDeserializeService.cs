using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace HexJson
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class JsonSerializableAttribute : Attribute { };

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
        /// Wether to use UTF-16 escape
        /// </summary>
        public bool StringEncoding { get; set; }
    };
    class JsonValueAccessMethods
    {
        /// <summary>
        /// Object indexer method
        /// </summary>
        public MethodInfo JsonObjectIndexer { get; set; }
        /// <summary>
        /// Array indexer method
        /// </summary>
        public MethodInfo JsonArrayIndexer { get; set; }
        /// <summary>
        /// Array count method
        /// </summary>
        public MethodInfo JsonArrayCount { get; set; }
        /// <summary>
        /// Intrinsic cast methods
        /// </summary>
        public Dictionary<Type,MethodInfo> JsonValueIntrinsicCast { get; set; }
    }
    class JsonSerializerEmitter
    {
        private readonly static ConcurrentDictionary<Type, (DynamicMethod, Delegate)> Caches
            = new ConcurrentDictionary<Type, (DynamicMethod, Delegate)>();
        private MethodInfo QueryOrCreateProcedure(Type type)
        {
            if (Caches.TryGetValue(type, out var value))
                return value.Item1;
            else
            {
                var emitter = new JsonSerializerEmitter(type);
                var serializer = emitter.Generate();
                Caches.TryAdd(type, (emitter.Method, serializer));
                return emitter.Method;
            }
        }
        private readonly LocalBuilder m_return_value;
        private readonly ILGenerator m_il;
        private readonly DynamicMethod m_method;
        private readonly Type m_type;
        JsonSerializerEmitter(Type target)
        {
            m_type = target;
            m_method = new DynamicMethod("", target, new Type[] { typeof(JsonObject) });
            m_il = m_method.GetILGenerator();

            m_return_value = m_il.DeclareLocal(target);
            m_il.Emit(OpCodes.Newobj, target.GetConstructor(Type.EmptyTypes));
            m_il.Emit(OpCodes.Stloc, m_return_value);
        }
        /// <summary>
        /// Do initial work
        /// </summary>
        static JsonSerializerEmitter()
        {
            var intrinsic = new Dictionary<Type, MethodInfo>();
            intrinsic.Add(typeof(int), typeof(JsonValue).GetMethod("AsInt"));
            intrinsic.Add(typeof(double), typeof(JsonValue).GetMethod("AsDouble"));
            intrinsic.Add(typeof(string), typeof(JsonValue).GetMethod("AsString"));
            intrinsic.Add(typeof(bool), typeof(JsonValue).GetMethod("AsBoolean"));
            intrinsic.Add(typeof(object), typeof(JsonValue).GetMethod("AsBoxed"));

            MethodGroup = new JsonValueAccessMethods()
            {
                JsonArrayIndexer = typeof(JsonArray).GetProperty("Item").GetGetMethod(),
                JsonArrayCount = typeof(JsonArray).GetProperty("Count").GetGetMethod(),
                JsonObjectIndexer = typeof(JsonObject).GetProperty("Item").GetGetMethod(),
                JsonValueIntrinsicCast = intrinsic
            };
        }
        /// <summary>
        /// Methods needed by emitting
        /// </summary>
        public static JsonValueAccessMethods MethodGroup { get; }
        /// <summary>
        /// Is Intrinsic type
        /// </summary>
        /// <param name="Target"></param>
        /// <returns></returns>
        public static bool IsJsonIntrinsicType(Type Target) =>
            Target.IsPrimitive || Target == typeof(string);
        /// <summary>
        /// generate method
        /// </summary>
        /// <returns></returns>
        public Delegate Generate()
        {
            if (m_type == null || m_type.GetCustomAttribute(typeof(JsonSerializableAttribute)) == null)
                return null;

            var properties = m_type.GetProperties
                (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(
                item => item.GetCustomAttribute(typeof(JsonFieldAttribute)) != null).Select(
                item => (item, item.GetCustomAttribute(typeof(JsonFieldAttribute)) as JsonFieldAttribute));

            foreach (var (property, attribute) in properties)
            {
                if (IsJsonIntrinsicType(property.PropertyType))
                    EmitIntrinsicPropertySet(attribute.JsonField, property);
                else
                    EmitPropertySet(property, () => EmitNonIntrinsicPreparation(attribute, property.PropertyType));
            }

            var fields = m_type.GetFields
                (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(
                item => item.GetCustomAttribute(typeof(JsonFieldAttribute)) != null).Select(
                item => (item, item.GetCustomAttribute(typeof(JsonFieldAttribute)) as JsonFieldAttribute));

            foreach (var (field, attribute) in fields)
            {
                if (IsJsonIntrinsicType(field.FieldType))
                    EmitIntrinsicFieldSet(attribute.JsonField, field);
                else
                    EmitFieldSet(field, () => EmitNonIntrinsicPreparation(attribute, field.FieldType));
            }

            m_il.Emit(OpCodes.Ldloc, m_return_value);
            m_il.Emit(OpCodes.Ret);

            //We need hacking to get the MethodHandle to PreJIT,and we guarantee it won't go out of our scope thus it's safe to do so
            var handle = (RuntimeMethodHandle)typeof(DynamicMethod)
                .GetMethod("GetMethodDescriptor", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(m_method, new object[0]);

            var ret = m_method.CreateDelegate(typeof(Func<,>).MakeGenericType(typeof(JsonObject), m_type));
            RuntimeHelpers.PrepareMethod(handle);
            return ret;
        }
        /// <summary>
        /// Target method
        /// </summary>
        public DynamicMethod Method => m_method;
        /// <summary>
        /// Emit code that traverses non-intrinsic JsonValue to managed object, the result is on the top of stack
        /// </summary>
        /// <param name="attribute"></param>
        /// <param name="info"></param>
        public void EmitNonIntrinsicPreparation(JsonFieldAttribute attribute, Type target)
        {
            if (target.IsArray)
                EmitJsonArrayPreparation(attribute.JsonField, target.GetElementType());
            else
            {
                EmitCallProcedure(QueryOrCreateProcedure(target), attribute.JsonField);
                EmitOnValueTypeUnbox(target);
            }
        }
        /// <summary>
        /// Emit code that traverses the json array to managed array, the result is on the top of stack
        /// </summary>
        /// <param name="jsonField"></param>
        /// <param name="type"></param>
        public void EmitJsonArrayPreparation(string jsonField, Type type, bool isNested = false)
        {
            var json_array = m_il.DeclareLocal(typeof(JsonArray));
            var array_length = m_il.DeclareLocal(typeof(int));
            var array = m_il.DeclareLocal(type.MakeArrayType());
            var cnt = m_il.DeclareLocal(typeof(int));
            var item = m_il.DeclareLocal(type);

            //decide where the JsonArray should come from      
            if (!isNested)
            {
                //json_array = (JsonArray)JsonObject.GetValue(jsonField)
                m_il.Emit(OpCodes.Ldarg_0);
                m_il.Emit(OpCodes.Ldstr, jsonField);
                m_il.EmitCall(OpCodes.Callvirt, MethodGroup.JsonObjectIndexer, null);
                EmitOnValueTypeUnbox(typeof(JsonArray));             
            }
            //otherwise we've got the JsonArray already on the stack in the previous call   
            //make duplicate of it and store it to json_array local
            m_il.Emit(OpCodes.Dup);
            m_il.Emit(OpCodes.Stloc, json_array);

            //remains one copy
            //array_length = json_array.Length
            m_il.EmitCall(OpCodes.Callvirt, MethodGroup.JsonArrayCount, null);
            m_il.Emit(OpCodes.Dup);
            m_il.Emit(OpCodes.Stloc, array_length);

            //array = new Type[array_length]
            m_il.Emit(OpCodes.Newarr, type);
            m_il.Emit(OpCodes.Stloc, array);

            var loop_start = m_il.DefineLabel();
            var loop_end = m_il.DefineLabel();

            //cnt = 0
            m_il.Emit(OpCodes.Ldc_I4, 0);
            m_il.Emit(OpCodes.Stloc, cnt);
            m_il.Emit(OpCodes.Br, loop_end);

            //item = json_array[cnt]
            m_il.MarkLabel(loop_start);

            m_il.Emit(OpCodes.Ldloc, json_array);
            m_il.Emit(OpCodes.Ldloc, cnt);
            m_il.EmitCall(OpCodes.Callvirt, MethodGroup.JsonArrayIndexer, null);

            if (IsJsonIntrinsicType(type))
            {
                EmitOnValueTypeUnbox(typeof(JsonValue));

                m_il.EmitCall(OpCodes.Callvirt, MethodGroup.JsonValueIntrinsicCast[type], null);
                m_il.Emit(OpCodes.Stloc, item);

                //array[cnt] = item
                m_il.Emit(OpCodes.Ldloc, array);
                m_il.Emit(OpCodes.Ldloc, cnt);
                m_il.Emit(OpCodes.Ldloc, item);
            }
            else
            {
                //we meet type like Type[]...[]
                if (type.IsArray)
                {
                    //firstly try cast the IJsonValue to JsonArray and push it to eval stack
                    EmitOnValueTypeUnbox(typeof(JsonArray));
                    //then call EmitJsonArrayPreparation with isNested being true
                    EmitJsonArrayPreparation(null, type.GetElementType(), true);
                    //store the result to item
                    m_il.Emit(OpCodes.Stloc, item);

                    m_il.Emit(OpCodes.Ldloc, array);
                    m_il.Emit(OpCodes.Ldloc, cnt);
                    m_il.Emit(OpCodes.Ldloc, item);
                }
                else
                {
                    //object_local = procedure(jsonObject)
                    var object_local = m_il.DeclareLocal(typeof(object));
                    EmitOnValueTypeUnbox(typeof(JsonObject));
                    m_il.EmitCall(OpCodes.Call, QueryOrCreateProcedure(type), null);
                    m_il.Emit(OpCodes.Stloc, object_local);

                    m_il.Emit(OpCodes.Ldloc, array);
                    m_il.Emit(OpCodes.Ldloc, cnt);
                    m_il.Emit(OpCodes.Ldloc, object_local);
                    EmitOnValueTypeUnbox(type);
                }
            }
            if (type.IsValueType)
                m_il.Emit(OpCodes.Stelem, type);
            else
                m_il.Emit(OpCodes.Stelem_Ref);

            //cnt = cnt + 1
            m_il.Emit(OpCodes.Ldloc, cnt);
            m_il.Emit(OpCodes.Ldc_I4_1);
            m_il.Emit(OpCodes.Add);
            m_il.Emit(OpCodes.Stloc, cnt);

            m_il.MarkLabel(loop_end);
            // cnt < array_length
            m_il.Emit(OpCodes.Ldloc, cnt);
            m_il.Emit(OpCodes.Ldloc, array_length);

            m_il.Emit(OpCodes.Blt, loop_start);

            m_il.Emit(OpCodes.Ldloc, array);
        }
        /// <summary>
        /// Emit code that calls non-intrinsic emitted method like fn(json_object). The result is on the top of stack
        /// </summary>
        /// <param name="method"></param>
        public void EmitCallProcedure(MethodInfo method,string jsonField)
        {
            m_il.Emit(OpCodes.Ldarg_0);
            m_il.Emit(OpCodes.Ldstr, jsonField);
            m_il.EmitCall(OpCodes.Callvirt, MethodGroup.JsonObjectIndexer, null);
            m_il.Emit(OpCodes.Castclass, typeof(JsonObject));
            m_il.EmitCall(OpCodes.Call, method, null);
        }
        /// <summary>
        /// Emit code that casts/unbox object on the top of stack to specific type
        /// </summary>
        /// <param name="type"></param>
        public void EmitOnValueTypeUnbox(Type type)
        {
            if (type.IsPrimitive)
                m_il.Emit(OpCodes.Unbox_Any, type);
            else
                m_il.Emit(OpCodes.Castclass, type);
        }
        /// <summary>
        /// Emit code that gets the json-intrinsic value, the result is on the top of stack
        /// </summary>
        /// <param name="jsonField"></param>
        /// <param name="type"></param>
        public void EmitJsonValuePreparation(string jsonField, Type type)
        {
            m_il.Emit(OpCodes.Ldarg_0);
            m_il.Emit(OpCodes.Ldstr, jsonField);
            m_il.EmitCall(OpCodes.Callvirt, MethodGroup.JsonObjectIndexer, null);
            m_il.Emit(OpCodes.Castclass, typeof(JsonValue));
            m_il.EmitCall(OpCodes.Callvirt, MethodGroup.JsonValueIntrinsicCast[type], null);
        }
        /// <summary>
        /// Emit code that gets the json-intrinsic value and sets the property
        /// </summary>
        /// <param name="jsonField"></param>
        /// <param name="property"></param>
        public void EmitIntrinsicPropertySet(string jsonField, PropertyInfo property)
            => EmitPropertySet(property, () => EmitJsonValuePreparation(jsonField, property.PropertyType));
        /// <summary>
        /// Emit code that gets the json-intrinsic value and sets the field
        /// </summary>
        /// <param name="jsonField"></param>
        /// <param name="field"></param>
        public void EmitIntrinsicFieldSet(string jsonField, FieldInfo field)
            => EmitFieldSet(field, () => EmitJsonValuePreparation(jsonField, field.FieldType));
        /// <summary>
        /// Emit code that sets the result of generator to target property of current object
        /// </summary>
        /// <param name="property"></param>
        /// <param name="generator"></param>
        public void EmitPropertySet(PropertyInfo property, Action generator)
        {
            m_il.Emit(OpCodes.Ldloc, m_return_value);
            generator();
            m_il.EmitCall(OpCodes.Callvirt, property.GetSetMethod(), null);
        }
        /// <summary>
        /// Emit code that sets the result of generator to target field of current object
        /// </summary>
        /// <param name="property"></param>
        /// <param name="generator"></param>
        public void EmitFieldSet(FieldInfo field, Action generator)
        {
            m_il.Emit(OpCodes.Ldloc, m_return_value);
            generator();
            m_il.Emit(OpCodes.Stfld, field);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static Delegate QueryDeserializer(Type target)
        {
            if(Caches.TryGetValue(target, out var item))
                return item.Item2;
            else
            {
                var emitter = new JsonSerializerEmitter(target);
                var serializer = emitter.Generate();
                Caches.TryAdd(target, (emitter.Method, serializer));
                return serializer;
            }
        }
    }
    public static partial class Json
    {
        /// <summary>
        /// Deserialize JsonObject to managed object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="target"></param>
        /// <returns></returns>
        public static T Deserialize<T>(JsonObject target)
            => QueryDeserializer<T>()(target);
        /// <summary>
        /// Query the serializer of T
        /// </summary>
        /// <typeparam name="T">target type</typeparam>
        /// <returns></returns>
        public static Func<JsonObject,T> QueryDeserializer<T>()
            => JsonSerializerEmitter.QueryDeserializer(typeof(T)) as Func<JsonObject, T>;
        /// <summary>
        /// Query the serializer of target
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static Delegate QueryDeserializer(Type target) 
            => JsonSerializerEmitter.QueryDeserializer(target);
    }
    namespace Old
    {
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
                if (ObjectType == null || ObjectType.GetCustomAttribute(typeof(JsonSerializableAttribute)) == null)
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
                        {
                            setter.JsonFields = Parse(setter.FieldType);
                        }
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
                            if (setter.JsonFields == null)
                                setter.Field.SetValue(ret, Convert.ChangeType(JsonTarget.GetValue(setter.JsonKey).AsBoxed(), setter.FieldType));
                            else
                                setter.Field.SetValue(ret, DeserializeObject(setter.FieldType, JsonTarget.GetObject(setter.JsonKey), setter.JsonFields));
                            break;
                        case FieldType.Property:
                            if (setter.JsonFields == null)
                                setter.Property.SetValue(ret, Convert.ChangeType(JsonTarget.GetValue(setter.JsonKey).AsBoxed(), setter.FieldType));
                            else
                                setter.Property.SetValue(ret, DeserializeObject(setter.FieldType, JsonTarget.GetObject(setter.JsonKey), setter.JsonFields));
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
                                        array.SetValue(Convert.ChangeType(json_array.GetValue(i).AsBoxed(), setter.NestedType), i);
                                }
                                else
                                {
                                    IList list = Activator.CreateInstance(setter.FieldType) as IList;
                                    value = list;
                                    foreach (var item in json_array)
                                        list.Add(Convert.ChangeType((item as JsonValue).AsBoxed(), setter.NestedType));
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
}
