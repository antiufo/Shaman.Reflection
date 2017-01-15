using System;
using System.Reflection;
using System.Collections.Generic;

namespace Shaman.Runtime
{
    public static partial class ReflectionHelper
    {
#if false
        private struct MethodTypePair : IEquatable<MethodTypePair>
        {
            public MethodInfo GenericMethod;
            public Type TypeArgument;
            public MethodTypePair(MethodInfo genericMethod, Type typeArgument)
            {
                this.GenericMethod = genericMethod;
                this.TypeArgument = typeArgument;
            }

            public bool Equals(MethodTypePair other)
            {
                return this.GenericMethod == other.GenericMethod &&
                    this.TypeArgument == other.TypeArgument;
            }

            public override bool Equals(object obj)
            {
                if (obj is MethodTypePair) return Equals((MethodTypePair)obj);
                return false;
            }
            public override int GetHashCode()
            {
                return GenericMethod.GetHashCode() ^ TypeArgument.GetHashCode();
            }
        }

        private struct TypePair : IEquatable<TypePair>
        {
            public Type GenericType;
            public Type TypeArgument;
            public TypePair(Type genericType, Type typeArgument)
            {
                this.GenericType = genericType;
                this.TypeArgument = typeArgument;
            }

            public bool Equals(TypePair other)
            {
                return this.GenericType == other.GenericType &&
                    this.TypeArgument == other.TypeArgument;
            }

            public override bool Equals(object obj)
            {
                if (obj is TypePair) return Equals((TypePair)obj);
                return false;
            }
            public override int GetHashCode()
            {
                return GenericType.GetHashCode() ^ TypeArgument.GetHashCode();
            }
        }
#endif
        private static Type[] OneTypeArray = new Type[1];
        private static Type[] TwoTypeArray = new Type[2];
        private static Dictionary<ValueTuple<Type, Type>, Type> GenericTypes = new Dictionary<ValueTuple<Type, Type>, Type>();
        private static Dictionary<ValueTuple<MethodInfo, Type>, MethodInfo> GenericMethods = new Dictionary<ValueTuple<MethodInfo, Type>, MethodInfo>();
        public static Type MakeGenericTypeFast(this Type t, Type singleType)
        {
            lock (GenericTypes)
            {
                var k = ValueTuple.Create(t, singleType);
                Type builtType;
                if (GenericTypes.TryGetValue(k, out builtType)) return builtType;
                OneTypeArray[0] = singleType;
                builtType = t.MakeGenericType(OneTypeArray);
                GenericTypes[k] = builtType;
                return builtType;
            }
        }
        public static Type MakeGenericTypeFast(this Type t, Type type1, Type type2)
        {
            lock (GenericTypes)
            {
                TwoTypeArray[0] = type1;
                TwoTypeArray[1] = type2;
                return t.MakeGenericType(TwoTypeArray);
            }
        }
        public static Type MakeGenericTypeFast(this Type t, params Type[] types)
        {
            return t.MakeGenericType(types);
        }
        public static MethodInfo MakeGenericMethodFast(this MethodInfo t, Type singleType)
        {
            lock (GenericTypes)
            {
                var k = ValueTuple.Create(t, singleType);
                MethodInfo builtMethod;
                if (GenericMethods.TryGetValue(k, out builtMethod)) return builtMethod;
                OneTypeArray[0] = singleType;
                builtMethod = t.MakeGenericMethod(OneTypeArray);
                GenericMethods[k] = builtMethod;
                return builtMethod;
            }
        }
        public static MethodInfo MakeGenericMethodFast(this MethodInfo t, Type type1, Type type2)
        {
            lock (GenericTypes)
            {
                TwoTypeArray[0] = type1;
                TwoTypeArray[1] = type2;
                return t.MakeGenericMethod(TwoTypeArray);
            }
        }
        public static MethodInfo MakeGenericMethodFast(this MethodInfo t, params Type[] types)
        {
            return t.MakeGenericMethod(types);
        }

    }

}