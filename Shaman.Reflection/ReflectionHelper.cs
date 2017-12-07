using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Numerics.Hashing;
using System.Threading.Tasks;
using Shaman.Runtime;

namespace Shaman.Runtime
{
    public static partial class ReflectionHelper
    {

        public static object InitializeWrapper(Type wrapper, Assembly targetAssembly, string typeName)
        {
            return InitializeWrapper(wrapper, targetAssembly.GetType(typeName, true, false));
        }


#if CORECLR
        private static Func<object, Assembly[]> AppDomain_GetAssemblies;
        private static Func<object> AppDomain_CurrentDomain;
#endif
        public static object InitializeWrapper(Type wrapper, string targetAssembly, string typeName)
        {
            var asm = targetAssembly.Contains(" ") ? Assembly.Load(new AssemblyName(targetAssembly)) : null;
            if (asm == null)
            {
#if CORECLR
                if (AppDomain_GetAssemblies == null)
                {
                    var mscorlib = typeof(string).GetTypeInfo().Assembly;
                    AppDomain_CurrentDomain = GetWrapper<Func<object>>(mscorlib, "System.AppDomain", "get_CurrentDomain");
                    AppDomain_GetAssemblies = GetWrapper<Func<object, Assembly[]>>(mscorlib, "System.AppDomain", "GetAssemblies");
                }
                asm = AppDomain_GetAssemblies(AppDomain_CurrentDomain()).First(x => x.GetName().Name == targetAssembly);
#else
                asm = AppDomain.CurrentDomain.GetAssemblies().First(x => x.GetName().Name == targetAssembly);
#endif
                if (asm == null) throw new ArgumentException("Cannot find assembly " + targetAssembly);
            }
            return InitializeWrapper(wrapper, asm, typeName);

        }

        public static object InitializeWrapper(Type wrapper, Type typeFromSameAssembly, string typeName)
        {
            return InitializeWrapper(wrapper, typeFromSameAssembly.GetTypeInfo().Assembly, typeName);
        }
        public static object InitializeWrapper(Type wrapper, Type wrappedType)
        {
            var members = wrappedType.GetMembers(AllBindingFlags).ToLookup(x => x.Name);
            foreach (var item in wrapper.GetFields(AllBindingFlags))
            {
                var ft = item.FieldType;
                if (ft != typeof(object))
                {
                    try
                    {
                        var value = GetWrapper(wrappedType, item.Name, item.FieldType, members);
                        item.SetValue(null, value);
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException("Cannot create reflection wrapper for " + wrappedType.FullName + "::" + item.Name, ex);
                    }
                }
            }

            return new object();
        }

        internal static BindingFlags AllBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        public static object GetWrapper(Type wrappedType, string name, Type wrapperShape, ILookup<string, MemberInfo> members = null, Type[] genericArguments = null)
        {
            if (members == null) members = wrappedType.GetMembers(AllBindingFlags).ToLookup(x =>
            {
                var n = x.Name;
                var idx = n.LastIndexOf("_Overload");
                if (idx != -1) n = n.Substring(0, idx);
                return n;
            });

            MemberInfo member = null;
            MemberInfo dup = null;
            try
            {
                var shapeComponents = wrapperShape.GetGenericArguments();
                if (name == "ctor") name = ".ctor";
                var candidates = members[name];

                if (candidates.Count() == 1) member = candidates.First();
                else
                {
                    foreach (var x in candidates)
                    {
                        var m = (MethodBase)x;
                        var parameters = m.GetParameters();
                        var methodinfo = m as MethodInfo;

                        var expectReturn = (methodinfo == null || methodinfo.ReturnType != typeof(void));

                        var funcName = wrapperShape.GetGenericTypeDefinition().ToString();
                        if (funcName.StartsWith("System.Func`") && !expectReturn) continue;
                        if (funcName.StartsWith("System.Action") && expectReturn) continue;

                        var expectThis = methodinfo != null && !methodinfo.IsStatic;

                        var expectedCount = (expectThis ? 1 : 0) + parameters.Length + (expectReturn ? 1 : 0);
                        if (expectedCount != shapeComponents.Length) continue;
                        var fail = false;
                        for (int i = expectThis ? 1 : 0, j = 0; i < shapeComponents.Length - (expectReturn ? 1 : 0); i++, j++)
                        {
                            var cmp = shapeComponents[i];
                            if (cmp == typeof(object)) continue;
                            var t = parameters[j].ParameterType;
                            if (t != cmp) { fail = true; break; }
                        }
                        if (fail) continue;
                        if (member != null) throw new Exception("Ambiguous reflected methods: " + member + ", " + x);
                        member = x;
                    }
                }
                if(member == null) throw new Exception("None of the candidate members meets the criteria.");
                var method = (MethodBase)member;
                if (genericArguments != null) method = ((MethodInfo)method).MakeGenericMethod(genericArguments);
                return GetWrapper(method, wrapperShape);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Cannot find member '" + wrappedType.FullName + "::" + name + "'.", ex);
            }
        }

        public static Func<TThis, TValue> GetGetter<TThis, TValue>(string fieldOrProperty)
        {
            var field = typeof(TThis).GetField(fieldOrProperty, AllBindingFlags);
            if (field != null) return GetGetter<TThis, TValue>(field);
            return GetGetter<TThis, TValue>(typeof(TThis).GetProperty(fieldOrProperty, AllBindingFlags));
        }
        public static Action<TThis, TValue> GetSetter<TThis, TValue>(string fieldOrProperty)
        {
            var field = typeof(TThis).GetField(fieldOrProperty, AllBindingFlags);
            if (field != null) return GetSetter<TThis, TValue>(field);
            return GetSetter<TThis, TValue>(typeof(TThis).GetProperty(fieldOrProperty, AllBindingFlags));
        }
        public static Func<TThis, TValue> GetGetter<TThis, TValue>(PropertyInfo property)
        {
            return GetWrapper<Func<TThis, TValue>>(property.GetMethod);
        }
        public static Action<TThis, TValue> GetSetter<TThis, TValue>(PropertyInfo property)
        {
            return GetWrapper<Action<TThis, TValue>>(property.SetMethod);
        }
        public static Func<TThis, TValue> GetGetter<TThis, TValue>(FieldInfo field)
        {
            return (Func<TThis, TValue>)GetGetter(field, typeof(Func<TThis, TValue>));
        }

        


        public static object GetGetter(FieldInfo field, Type wrapperShape)
        {
            var gen = wrapperShape.GetGenericArguments();
            if (field.IsStatic)
            {
                return Expression.Lambda(MaybeConvert(Expression.Field(null, field), gen[gen.Length - 1])).Compile();
            }
            else
            {
                var p = Expression.Parameter(gen[0]);
                return Expression.Lambda(MaybeConvert(Expression.Field(MaybeConvert(p, field.DeclaringType), field), gen[gen.Length - 1]), p).Compile();
            }
        }

        public static Action<TThis, TValue> GetSetter<TThis, TValue>(FieldInfo field)
        {
            return (Action<TThis, TValue>)GetSetter(field, typeof(Action<TThis, TValue>));
        }

        public static object GetSetter(FieldInfo field, Type wrapperShape)
        {
            var gen = wrapperShape.GetGenericArguments();
            var value = Expression.Parameter(gen[gen.Length - 1]);
            if (field.IsStatic)
            {
                return Expression.Lambda(Expression.Block(Expression.Assign(Expression.Field(null, field), MaybeConvert(value, field.FieldType)), Expression.Empty()), value).Compile();
            }
            else
            {

                var t = Expression.Parameter(gen[0]);
                return Expression.Lambda(Expression.Block(Expression.Assign(Expression.Field(MaybeConvert(t, field.DeclaringType), field), MaybeConvert(value, field.FieldType)), Expression.Empty()), t, value).Compile();
            }
        }

        public static TDelegate GetWrapper<TDelegate>(MethodBase methodBase)
        {
            return (TDelegate)GetWrapper(methodBase, typeof(TDelegate));
        }

        public static TDelegate GetWrapper<TDelegate>(Assembly assembly, string typeName, string methodName)
        {
            return GetWrapper<TDelegate>(assembly, typeName, methodName, null);
        }


        public static TDelegate GetWrapper<TDelegate>(Assembly assembly, string typeName, string methodName, Type[] genericArguments)
        {
            return GetWrapper<TDelegate>(assembly.GetType(typeName, true, false), methodName, genericArguments);
        }

        public static TDelegate GetWrapper<TDelegate>(Type wrappedType, string methodName)
        {
            return GetWrapper<TDelegate>(wrappedType, methodName, null);

        }

        public static TDelegate GetWrapper<TDelegate>(Type wrappedType, string methodName, Type[] genericArguments)
        {
            return (TDelegate)GetWrapper(wrappedType, methodName, typeof(TDelegate), null, genericArguments);
        }

        public static object GetWrapper(MethodBase methodBase, Type wrapperShape)
        {
            Type[] shape;
            var isaction = wrapperShape.FullName.StartsWith("System.Action");
            var isfunc = wrapperShape.FullName.StartsWith("System.Func`");
            if (!isaction && !isfunc)
            {
                var invoke = wrapperShape.GetMethod("Invoke");
                isaction = invoke.ReturnType == typeof(void);
                var p = invoke.GetParameters();
                shape = new Type[p.Length + (isaction ? 0 : 1)];
                for (int i = 0; i < p.Length; i++)
                {
                    shape[i] = p[i].ParameterType;
                }
                if (!isaction) shape[shape.Length - 1] = invoke.ReturnType;
            }
            else
            {
                shape = wrapperShape.GetGenericArguments();
            }
            var parameters = (isaction ? shape : shape.Take(shape.Length - 1))
                .Select(x => Expression.Parameter(x)).ToList();


            var body = GetWrapperBody(methodBase, parameters);
            if (isaction && body.Type != typeof(void)) body = Expression.Block(body, Expression.Empty());
            else if (!isaction)
            {
                var returnType = shape[shape.Length - 1];
                if (returnType != body.Type)
                {
                    if (returnType == typeof(Task<object>))
                    {
                        if (ConvertToTaskOfObjectMethod == null)
                        {
                            ConvertToTaskOfObjectMethod = typeof(ReflectionHelper).GetMethod("ConvertToTaskOfObject", AllBindingFlags);
                        }
                        var c = ConvertToTaskOfObjectMethod.MakeGenericMethod(body.Type.GetGenericArguments()[0]);

                        body = Expression.Call(c, body);
                    }
                    else
                    {
                        body = Expression.Convert(body, returnType);
                    }
                }
            }

            return Expression.Lambda(wrapperShape, body, parameters).Compile();
        }

        private static Expression GetWrapperBody(MethodBase methodBase, List<ParameterExpression> parameters)
        {
            var methodinfo = methodBase as MethodInfo;
            var reflectionParameters = methodBase.GetParameters();
            if (methodinfo == null)
            {
                return Expression.New((ConstructorInfo)methodBase, parameters.Select((x, i) => MaybeConvert(x, reflectionParameters[i])));
            }

            if (methodBase.IsStatic)
            {
                return Expression.Call(methodinfo, parameters.Select((x, i) => MaybeConvert(x, reflectionParameters[i])));
            }
            else
            {
                return Expression.Call(MaybeConvert(parameters[0], methodBase.DeclaringType), methodinfo, parameters.Skip(1).Select((x, i) => MaybeConvert(x, reflectionParameters[i])));
            }
        }

        private static Expression MaybeConvert(Expression x, Type type)
        {
            if (x.Type == type) return x;
            return Expression.Convert(x, type);
        }

        private static Expression MaybeConvert(Expression x, ParameterInfo parameterInfo)
        {
            return MaybeConvert(x, parameterInfo.ParameterType);
        }

        private static MethodInfo ConvertToTaskOfObjectMethod;

        private static async Task<object> ConvertToTaskOfObject<T>(Task<T> task)
        {
            return await task;
        }





    }
}

namespace Shaman.Runtime.ReflectionExtensions
{
    internal struct ArrayOfType : IEquatable<ArrayOfType>
    {

        public ArrayOfType(Type[] t)
        {
            this.Array = t;
            this.HashCode = 0;
            for (int i = 0; i < t.Length; i++)
            {
                this.HashCode = HashHelpers.Combine(this.HashCode, t[i].GetHashCode());
            }
        }
        public override int GetHashCode()
        {
            return HashCode;
        }
        public override bool Equals(object other)
        {
            if (!(other is ArrayOfType)) return false;
            return Equals((ArrayOfType)other);
        }
        public bool Equals(ArrayOfType other)
        {
            if (other.Array.Length != this.Array.Length) return false;
            for (int i = 0; i < this.Array.Length; i++)
            {
                if (other.Array[i] != this.Array[i]) return false;
            }
            return true;
        }
        public readonly Type[] Array;
        public readonly int HashCode;
    }
    public static class ReflectionExtensionMethods
    {
        private static Dictionary<ValueTuple<Type, Type, string>, Delegate> fieldGetCache = new Dictionary<ValueTuple<Type, Type, string>, Delegate>();
        private static Dictionary<ValueTuple<Type, Type, string>, Delegate> fieldSetCache = new Dictionary<ValueTuple<Type, Type, string>, Delegate>();
        private static Dictionary<ValueTuple<Type, string, ArrayOfType>, object> methodCache = new Dictionary<ValueTuple<Type, string, ArrayOfType>, object>();

        public static object GetFieldOrProperty(this object obj, string name)
        {
            return GetFieldOrProperty<object>(obj, name);
        }
        public static TReturn GetFieldOrProperty<TReturn>(this object obj, string name)
        {
            if (obj == null) throw new ArgumentNullException();
            var stat = IsType(obj);
            var type = stat ? (Type)obj : obj.GetType();
            var key = ValueTuple.Create(type, typeof(TReturn), name);

            Delegate d;
            lock (fieldGetCache)
            {
                if (!fieldGetCache.TryGetValue(key, out d))
                {
                    
                    var field = type.GetField(name, ReflectionHelper.AllBindingFlags);
                    var t = stat ? typeof(Func<TReturn>) : typeof(Func<object, TReturn>);
                    if (field != null) d = (Delegate)ReflectionHelper.GetGetter(field, t);
                    else d = (Delegate)ReflectionHelper.GetWrapper(type.GetProperty(name, ReflectionHelper.AllBindingFlags).GetMethod, t);
                    fieldGetCache[key] = d;
                }
            }
            return stat ?
                ((Func<TReturn>)d)() : 
                ((Func<object, TReturn>)d)(obj);
        }
        public static void SetFieldOrProperty<TValue>(this object obj, string name, TValue value)
        {
            if (obj == null) throw new ArgumentNullException();
            var stat = IsType(obj);
            var type = stat ? (Type)obj : obj.GetType();
            var key = ValueTuple.Create(type, typeof(TValue), name);

            Delegate d;
            lock (fieldSetCache)
            {
                if (!fieldSetCache.TryGetValue(key, out d))
                {
                    var field = type.GetField(name, ReflectionHelper.AllBindingFlags);
                    var t = stat ? typeof(Action<TValue>) : typeof(Action<object, TValue>);
                    if (field != null) d = (Delegate)ReflectionHelper.GetSetter(field, t);
                    else d = (Delegate)ReflectionHelper.GetWrapper(type.GetProperty(name, ReflectionHelper.AllBindingFlags).SetMethod, t);
                    fieldSetCache[key] = d;
                }
            }
            if(stat) ((Action<TValue>)d)(value);
            else ((Action<object, TValue>)d)(obj, value);
        }

        private static bool IsType(object o)
        {
            return o is Type;
        }

        public static void InvokeAction(this object obj, string name)
        {
            var arr = new Type[] { typeof(void) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Action>(obj, name, arr);
                action();
            }
            else
            {
                var action = GetImmediateWrapper<Action<object>>(obj, name, arr);
                action(obj);
            }
        }
        public static void InvokeAction<T1>(this object obj, string name, T1 arg1)
        {
            var arr = new[] { typeof(T1), typeof(void) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Action<T1>>(obj, name, arr);
                action(arg1);
            }
            else
            {
                var action = GetImmediateWrapper<Action<object, T1>>(obj, name, arr);
                action(obj, arg1);
            }
        }
        public static void InvokeAction<T1, T2>(this object obj, string name, T1 arg1, T2 arg2)
        {
            var arr = new[] { typeof(T1), typeof(T2), typeof(void) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Action<T1, T2>>(obj, name, arr);
                action(arg1, arg2);
            }
            else
            {
                var action = GetImmediateWrapper<Action<object, T1, T2>>(obj, name, arr);
                action(obj, arg1, arg2);
            }
        }
        public static void InvokeAction<T1, T2, T3>(this object obj, string name, T1 arg1, T2 arg2, T3 arg3)
        {
            var arr = new[] { typeof(T1), typeof(T2), typeof(T3), typeof(void) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Action<T1, T2, T3>>(obj, name, arr);
                action(arg1, arg2, arg3);
            }
            else
            {
                var action = GetImmediateWrapper<Action<object, T1, T2, T3>>(obj, name, arr);
                action(obj, arg1, arg2, arg3);
            }
        }
        public static void InvokeAction<T1, T2, T3, T4>(this object obj, string name, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            var arr = new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(void) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Action<T1, T2, T3, T4>>(obj, name, arr);
                action(arg1, arg2, arg3, arg4);
            }
            else
            {
                var action = GetImmediateWrapper<Action<object, T1, T2, T3, T4>>(obj, name, arr);
                action(obj, arg1, arg2, arg3, arg4);
            }
        }
        public static void InvokeAction<T1, T2, T3, T4, T5>(this object obj, string name, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            var arr = new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(void) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Action<T1, T2, T3, T4, T5>>(obj, name, arr);
                action(arg1, arg2, arg3, arg4, arg5);
            }
            else
            {
                var action = GetImmediateWrapper<Action<object, T1, T2, T3, T4, T5>>(obj, name, arr);
                action(obj, arg1, arg2, arg3, arg4, arg5);
            }
        }
        public static void InvokeAction<T1, T2, T3, T4, T5, T6>(this object obj, string name, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            var arr = new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(void) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Action<T1, T2, T3, T4, T5, T6>>(obj, name, arr);
                action(arg1, arg2, arg3, arg4, arg5, arg6);
            }
            else
            {
                var action = GetImmediateWrapper<Action<object, T1, T2, T3, T4, T5, T6>>(obj, name, arr);
                action(obj, arg1, arg2, arg3, arg4, arg5, arg6);
            }
        }



        public static object InvokeFunction(this object obj, string name)
        {
            var arr = new[] { typeof(object) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Func<object>>(obj, name, arr);
                return action();
            }
            else
            {
                var action = GetImmediateWrapper<Func<object, object>>(obj, name, arr);
                return action(obj);
            }
        }
        public static object InvokeFunction<T1>(this object obj, string name, T1 arg1)
        {
            var arr = new[] { typeof(T1), typeof(object) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Func<T1, object>>(obj, name, arr);
                return action(arg1);
            }
            else
            {
                var action = GetImmediateWrapper<Func<object, T1, object>>(obj, name, arr);
                return action(obj, arg1);
            }
        }
        public static object InvokeFunction<T1, T2>(this object obj, string name, T1 arg1, T2 arg2)
        {
            var arr = new[] { typeof(T1), typeof(T2), typeof(object) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Func<T1, T2, object>>(obj, name, arr);
                return action(arg1, arg2);
            }
            else
            {
                var action = GetImmediateWrapper<Func<object, T1, T2, object>>(obj, name, arr);
                return action(obj, arg1, arg2);
            }

        }
        public static object InvokeFunction<T1, T2, T3>(this object obj, string name, T1 arg1, T2 arg2, T3 arg3)
        {
            var arr = new[] { typeof(T1), typeof(T2), typeof(T3), typeof(object) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Func<T1, T2, T3, object>>(obj, name, arr);
                return action(arg1, arg2, arg3);
            }
            else
            {
                var action = GetImmediateWrapper<Func<object, T1, T2, T3, object>>(obj, name, arr);
                return action(obj, arg1, arg2, arg3);
            }
        }
        public static object InvokeFunction<T1, T2, T3, T4>(this object obj, string name, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            var arr = new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(object) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Func<T1, T2, T3, T4, object>>(obj, name, arr);
                return action(arg1, arg2, arg3, arg4);
            }
            else
            {
                var action = GetImmediateWrapper<Func<object, T1, T2, T3, T4, object>>(obj, name, arr);
                return action(obj, arg1, arg2, arg3, arg4);
            }
        }
        public static object InvokeFunction<T1, T2, T3, T4, T5>(this object obj, string name, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            var arr = new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(object) };
            if (IsType(obj))
            {

                var action = GetImmediateWrapper<Func<T1, T2, T3, T4, T5, object>>(obj, name, arr);
                return action(arg1, arg2, arg3, arg4, arg5);
            }
            else
            {
                var action = GetImmediateWrapper<Func<object, T1, T2, T3, T4, T5, object>>(obj, name, arr);
                return action(obj, arg1, arg2, arg3, arg4, arg5);
            }
        }
        public static object InvokeFunction<T1, T2, T3, T4, T5, T6>(this object obj, string name, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            var arr = new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(object) };
            if (IsType(obj))
            {
                var action = GetImmediateWrapper<Func<T1, T2, T3, T4, T5, T6, object>>(obj, name, arr);
                return action(arg1, arg2, arg3, arg4, arg5, arg6);
            }
            else
            {
                var action = GetImmediateWrapper<Func<object, T1, T2, T3, T4, T5, T6, object>>(obj, name, arr);
                return action(obj, arg1, arg2, arg3, arg4, arg5, arg6);
            }
        }






        internal static TDelegate GetImmediateWrapper<TDelegate>(object obj, string name, Type[] types)
        {
            if (obj == null) throw new ArgumentNullException();
            var type = obj as Type;
            bool stat;
            if (type == null)
            {
                stat = false;
                type = obj.GetType();
            }
            else
            {
                stat = true;
            }
            var key = ValueTuple.Create(type, name, new ArrayOfType(types));

            object d;
            lock (methodCache)
            {
                if (!methodCache.TryGetValue(key, out d))
                {
                    d = ReflectionHelper.GetWrapper(type, name, typeof(TDelegate), null, null);
                    methodCache[key] = d;
                }
            }
            return (TDelegate)d;
        }

    }

}