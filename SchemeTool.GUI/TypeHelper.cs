using System;
using System.Linq;

namespace SchemeEditor
{
    public static class TypeHelper
    {
        public static Type[] GetDerivedTypes(Type baseType)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly =>
                {
                    try
                    {
                        return assembly.GetTypes();
                    }
                    catch
                    {
                        return new Type[0];
                    }
                })
                .Where(type => type.IsClass && !type.IsAbstract && baseType.IsAssignableFrom(type))
                .OrderBy(t => t.Name)
                .ToArray();
        }

        public static bool IsCollectionType(Type type)
        {
            return type != typeof(string) && 
                   type != typeof(byte[]) &&
                   (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) ||
                    type.IsArray);
        }

        public static bool IsDictionaryType(Type type)
        {
            return typeof(System.Collections.IDictionary).IsAssignableFrom(type) && 
                   type != typeof(string);
        }

        public static bool IsListType(Type type)
        {
            return typeof(System.Collections.IList).IsAssignableFrom(type) && 
                   type != typeof(string) && 
                   type != typeof(byte[]);
        }

        public static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive || 
                   type == typeof(string) || 
                   type == typeof(decimal) || 
                   type == typeof(DateTime) || 
                   type == typeof(DateTimeOffset) || 
                   type == typeof(TimeSpan) || 
                   type == typeof(Guid);
        }

        public static Type GetCollectionItemType(Type collectionType)
        {
            if (collectionType.IsArray)
                return collectionType.GetElementType();

            if (collectionType.IsGenericType)
            {
                var genericArgs = collectionType.GetGenericArguments();
                if (genericArgs.Length > 0)
                    return genericArgs[0];
            }

            // Try to find IEnumerable<T> interface
            var enumerableInterface = collectionType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && 
                                    i.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IEnumerable<>));

            if (enumerableInterface != null)
                return enumerableInterface.GetGenericArguments()[0];

            return typeof(object);
        }

        public static (Type keyType, Type valueType) GetDictionaryTypes(Type dictionaryType)
        {
            if (dictionaryType.IsGenericType)
            {
                var genericArgs = dictionaryType.GetGenericArguments();
                if (genericArgs.Length >= 2)
                    return (genericArgs[0], genericArgs[1]);
            }

            // Try to find IDictionary<K,V> interface
            var dictInterface = dictionaryType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && 
                                    i.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IDictionary<,>));

            if (dictInterface != null)
            {
                var genericArgs = dictInterface.GetGenericArguments();
                return (genericArgs[0], genericArgs[1]);
            }

            return (typeof(object), typeof(object));
        }
    }
}