namespace Lucene.Net.Util
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    public static class TypeExtensions
    {
        private const BindingFlags DefaultFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;

#if DNXCORE50
        public static bool IsAssignableFrom(this Type type, Type c)
        {
            return type.GetTypeInfo().IsAssignableFrom(c.GetTypeInfo());
        }

        public static System.Attribute[] GetCustomAttributes(this Type type, Type attributeType, bool inherit)
        {
            return type
                .GetTypeInfo()
                .GetCustomAttributes(attributeType, inherit)
                .ToArray();
        }
#endif

        public static string CodeBase(this Assembly assembly)
        {
#if !DNXCORE50
            return assembly.CodeBase;
#else
            throw new NotImplementedException();
#endif
        }

        public static bool IsInterface(this Type type)
        {
#if !DNXCORE50
            return type.IsInterface;
#else
            return type.GetTypeInfo().IsInterface;
#endif
        }

        public static bool IsValueType(this Type type)
        {
#if !DNXCORE50
            return type.IsValueType;
#else
            return type.GetTypeInfo().IsValueType;
#endif
        }

        public static Type BaseType(this Type type)
        {
#if !DNXCORE50
            return type.BaseType;
#else
            return type.GetTypeInfo().BaseType;
#endif
        }

        public static Assembly Assembly(this Type type)
        {
#if !DNXCORE50
            return type.Assembly;
#else
            return type.GetTypeInfo().Assembly;
#endif
        }

        public static bool IsPublic(this Type type)
        {
#if !DNXCORE50
            return type.IsPublic;
#else
            return type.GetTypeInfo().IsPublic;
#endif
        }

        public static bool IsPrimitive(this Type type)
        {
#if !DNXCORE50
            return type.IsPrimitive;
#else
            return type.GetTypeInfo().IsPrimitive;
#endif
        }

#if DNXCORE50
        public static MethodInfo GetMethod(this Type type, string name, IList<Type> parameterTypes)
        {
            return type.GetMethod(name, DefaultFlags, null, parameterTypes, null);
        }

        public static MethodInfo GetMethod(this Type type, string name, BindingFlags bindingFlags, object placeHolder1, IList<Type> parameterTypes, object placeHolder2)
        {
            return type.GetTypeInfo().DeclaredMethods.Where(m =>
            {
                if (name != null && m.Name != name)
                    return false;

                if (!TestAccessibility(m, bindingFlags))
                    return false;

                return m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameterTypes);
            }).SingleOrDefault();
        }

        public static FieldInfo[] GetFields(this Type type, BindingFlags bindingFlags)
        {
            IList<FieldInfo> fields = bindingFlags.HasFlag(BindingFlags.DeclaredOnly)
                                          ? type.GetTypeInfo().DeclaredFields.ToList()
                                          : type.GetTypeInfo().GetFieldsRecursive();

            return fields.Where(f => TestAccessibility(f, bindingFlags)).ToArray();
        }
#endif

        private static bool TestAccessibility(MemberInfo member, BindingFlags bindingFlags)
        {
            if (member is FieldInfo)
            {
                return TestAccessibility((FieldInfo)member, bindingFlags);
            }
            else if (member is MethodBase)
            {
                return TestAccessibility((MethodBase)member, bindingFlags);
            }
            else if (member is PropertyInfo)
            {
                return TestAccessibility((PropertyInfo)member, bindingFlags);
            }

            throw new Exception("Unexpected member type.");
        }

        private static bool TestAccessibility(FieldInfo member, BindingFlags bindingFlags)
        {
            bool visibility = (member.IsPublic && bindingFlags.HasFlag(BindingFlags.Public)) ||
                              (!member.IsPublic && bindingFlags.HasFlag(BindingFlags.NonPublic));

            bool instance = (member.IsStatic && bindingFlags.HasFlag(BindingFlags.Static)) ||
                            (!member.IsStatic && bindingFlags.HasFlag(BindingFlags.Instance));

            return visibility && instance;
        }

        private static bool TestAccessibility(MethodBase member, BindingFlags bindingFlags)
        {
            bool visibility = (member.IsPublic && bindingFlags.HasFlag(BindingFlags.Public)) ||
                              (!member.IsPublic && bindingFlags.HasFlag(BindingFlags.NonPublic));

            bool instance = (member.IsStatic && bindingFlags.HasFlag(BindingFlags.Static)) ||
                            (!member.IsStatic && bindingFlags.HasFlag(BindingFlags.Instance));

            return visibility && instance;
        }

#if DNXCORE50
        private static IList<FieldInfo> GetFieldsRecursive(this TypeInfo type)
        {
            TypeInfo t = type;
            IList<FieldInfo> fields = new List<FieldInfo>();
            while (t != null)
            {
                foreach (var member in t.DeclaredFields)
                {
                    if (!fields.Any(p => p.Name == member.Name))
                        fields.Add(member);
                }
                t = (t.BaseType != null) ? t.BaseType.GetTypeInfo() : null;
            }

            return fields;
        }
#endif
    }
}