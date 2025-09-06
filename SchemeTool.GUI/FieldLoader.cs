using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace SchemeEditor
{
    public class FieldLoader
    {
        private readonly MainWindow mainWindow;
        private readonly FieldEditor fieldEditor;

        public FieldLoader(MainWindow window)
        {
            mainWindow = window;
            fieldEditor = new FieldEditor(window);
        }

        public void LoadFields(StackPanel container, Type type, object instance,
            Dictionary<string, object> fieldValues, Dictionary<FrameworkElement, object> originalValues)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties.Where(p => p.CanRead && p.CanWrite))
            {
                fieldEditor.AddFieldEditor(container, prop.Name, prop.PropertyType,
                    prop.GetValue(instance), fieldValues, originalValues, 0);
            }

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                fieldEditor.AddFieldEditor(container, field.Name, field.FieldType,
                    field.GetValue(instance), fieldValues, originalValues, 0);
            }
        }

        public void LoadComplexTypeFields(StackPanel container, string parentFieldName,
            object instance, int indentLevel, Dictionary<string, object> fieldValues,
            Dictionary<FrameworkElement, object> originalValues, Border parentContainer)
        {
            if (instance == null) return;

            var type = instance.GetType();

            // Special handling for collections
            if (instance is System.Collections.IDictionary dict)
            {
                var dictEditor = fieldEditor.CreateDictionaryEditor(parentFieldName, type, dict, indentLevel, parentContainer);
                container.Children.Add(dictEditor);
                return;
            }
            else if (instance is System.Collections.IList list && !(instance is byte[]))
            {
                var listEditor = fieldEditor.CreateListEditor(parentFieldName, type, list, indentLevel, parentContainer);
                container.Children.Add(listEditor);
                return;
            }
            else if (instance is System.Collections.IEnumerable enumerable &&
                !(instance is string) && !(instance is byte[]))
            {
                var enumerableEditor = fieldEditor.CreateEnumerableEditor(parentFieldName, type, enumerable, indentLevel, parentContainer);
                container.Children.Add(enumerableEditor);
                return;
            }

            if (!string.IsNullOrEmpty(parentFieldName))
                fieldValues[parentFieldName] = instance;

            // For non-collection types, show fields and properties
            var processedNames = new HashSet<string>();

            // Fields
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.Name.Contains("<") || field.Name.Contains(">") ||
                    field.Name.Contains("k__BackingField") || field.IsStatic)
                    continue;

                if (processedNames.Contains(field.Name))
                    continue;

                processedNames.Add(field.Name);

                try
                {
                    var fieldValue = field.GetValue(instance);
                    var fieldContainer = fieldEditor.CreateInlineFieldEditor(
                        field.Name,
                        field.FieldType,
                        fieldValue,
                        (name, value) =>
                        {
                            field.SetValue(instance, value);
                            // Update the parent field in fieldValues
                            if (!string.IsNullOrEmpty(parentFieldName))
                                fieldValues[parentFieldName] = instance;
                            // Mark parent container as edited
                            if (parentContainer != null)
                                fieldEditor.MarkFieldAsEdited(parentContainer);
                        },
                        indentLevel,
                        originalValues,
                        parentContainer
                    );
                    container.Children.Add(fieldContainer);
                }
                catch { }
            }

            // Properties
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var skipProperties = new HashSet<string> {
                "SyncRoot", "IsSynchronized", "IsReadOnly", "IsFixedSize",
                "Capacity", "Count", "Length", "Keys", "Values", "Item"
            };

            foreach (var prop in properties)
            {
                if (processedNames.Contains(prop.Name) ||
                    prop.GetIndexParameters().Length > 0 ||
                    skipProperties.Contains(prop.Name))
                    continue;

                var getMethod = prop.GetGetMethod();
                if (getMethod != null && getMethod.IsVirtual && type.IsAbstract)
                    continue;

                try
                {
                    if (!prop.CanRead)
                        continue;

                    if (prop.PropertyType == type || prop.PropertyType.IsAssignableFrom(type))
                        continue;

                    var propValue = prop.GetValue(instance, null);

                    if (prop.CanWrite && !getMethod.IsVirtual)
                    {
                        var propContainer = fieldEditor.CreateInlineFieldEditor(
                            prop.Name,
                            prop.PropertyType,
                            propValue,
                            (name, value) =>
                            {
                                prop.SetValue(instance, value, null);

                                if (!string.IsNullOrEmpty(parentFieldName))
                                    fieldValues[parentFieldName] = instance;
                                // Mark parent container as edited
                                if (parentContainer != null)
                                    fieldEditor.MarkFieldAsEdited(parentContainer);
                            },
                            indentLevel,
                            originalValues,
                            parentContainer
                        );
                        container.Children.Add(propContainer);
                    }
                }
                catch { }
            }
        }
    }
}