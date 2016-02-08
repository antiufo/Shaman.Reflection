using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Shaman.Runtime
{
    public static class ReflectionHelper
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

        private static BindingFlags AllBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        public static object GetWrapper(Type wrappedType, string name, Type wrapperShape, ILookup<string, MemberInfo> members = null, Type[] genericArguments = null)
        {
            if (members == null) members = wrappedType.GetMembers(AllBindingFlags).ToLookup(x => x.Name);

            try
            {
                var shapeComponents = wrapperShape.GetGenericArguments();
                if (name == "ctor") name = ".ctor";
                var candidates = members[name];
                var member =
                    candidates.Count() == 1 ? candidates.First() :
                    candidates.Single(x =>
                {
                    var m = (MethodBase)x;
                    var parameters = m.GetParameters();
                    var methodinfo = m as MethodInfo;

                    var expectReturn = (methodinfo == null || methodinfo.ReturnType != typeof(void));

                    var funcName = wrapperShape.GetGenericTypeDefinition().ToString();
                    if (funcName.StartsWith("System.Func`") && !expectReturn) return false;
                    if (funcName.StartsWith("System.Action") && expectReturn) return false;

                    var expectThis = methodinfo != null && !methodinfo.IsStatic;

                    var expectedCount = (expectThis ? 1 : 0) + parameters.Length + (expectReturn ? 1 : 0);
                    if (expectedCount != shapeComponents.Length) return false;
                    for (int i = expectThis ? 1 : 0, j = 0; i < shapeComponents.Length - (expectReturn ? 1 : 0); i++, j++)
                    {
                        var cmp = shapeComponents[i];
                        if (cmp == typeof(object)) continue;
                        var t = parameters[j].ParameterType;
                        if (t != cmp) return false;
                    }
                    return true;
                });
                var method = (MethodBase)member;
                if (genericArguments != null) method = ((MethodInfo)method).MakeGenericMethod(genericArguments);
                return GetWrapper(method, wrapperShape);
            }
            catch(Exception ex)
            {
                throw new ArgumentException("Cannot find member '"+wrappedType.FullName+"::"+name+"'.", ex);
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


        public static TDelegate GetWrapper<TDelegate>(Assembly assembly, string typeName, string methodName, Type[] genericArguments = null)
        {
            return GetWrapper<TDelegate>(assembly.GetType(typeName, true, false), methodName, genericArguments);
        }

        public static TDelegate GetWrapper<TDelegate>(Type wrappedType, string methodName, Type[] genericArguments = null)
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
