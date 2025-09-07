// FieldEditor.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Reflection;

namespace SchemeEditor
{
    public partial class FieldEditor
    {
        private readonly MainWindow mainWindow;
        private readonly ControlFactory controlFactory;

        public FieldEditor(MainWindow window)
        {
            mainWindow = window;
            controlFactory = new ControlFactory();
        }

        public void MarkFieldAsEdited(Border container)
        {
            container.BorderBrush = Brushes.Orange;
            container.BorderThickness = new Thickness(2);

            var setHasChangesMethod = mainWindow.GetType()
                .GetMethod("SetHasChanges", BindingFlags.Public | BindingFlags.Instance);
            setHasChangesMethod?.Invoke(mainWindow, new object[] { true });
        }

        private void MarkFieldAsConfirmed(Border container)
        {
            container.BorderBrush = Brushes.Green;
            container.BorderThickness = new Thickness(2);
            ChangeTextColor(container, Brushes.DarkBlue);
        }

        private void MarkFieldAsNormal(Border container)
        {
            container.BorderBrush = Brushes.LightGray;
            container.BorderThickness = new Thickness(1);
            ChangeTextColor(container, Brushes.Black);
        }

        private void ChangeTextColor(DependencyObject parent, Brush color)
        {
            if (parent == null) return;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock textBlock && !textBlock.Name.StartsWith("label"))
                    textBlock.Foreground = color;
                else if (child is TextBox textBox)
                    textBox.Foreground = color;
                else if (child is ComboBox comboBox)
                    comboBox.Foreground = color;
                else if (child is CheckBox checkBox)
                    checkBox.Foreground = color;

                ChangeTextColor(child, color);
            }
        }

        public void AddFieldEditor(StackPanel container, string fieldName, Type fieldType, 
            object value, Dictionary<string, object> fieldValues, 
            Dictionary<FrameworkElement, object> originalValues, int indentLevel = 0)
        {
            var border = controlFactory.CreateFieldContainer(indentLevel);
            var grid = controlFactory.CreateFieldGrid();

            originalValues[border] = value;

            var label = controlFactory.CreateLabel(fieldName + ":");
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            FrameworkElement editor = CreateEditorForType(fieldName, fieldType, value, 
                fieldValues, originalValues, indentLevel, border);

            if (editor != null)
            {
                Grid.SetColumn(editor, 1);
                grid.Children.Add(editor);
            }

            var buttonPanel = CreateActionButtons(fieldName, fieldType, fieldValues, originalValues, 
                border, container);
            Grid.SetColumn(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            border.Child = grid;
            container.Children.Add(border);

            if (value != null)
                fieldValues[fieldName] = value;
        }

        private void SafeAddToContainer(Panel container, UIElement element, int? index = null)
        {
            ErrorHandler.SafeExecute(() =>
            {
                if (index.HasValue && index.Value >= 0 && index.Value < container.Children.Count)
                    container.Children.Insert(index.Value, element);
                else
                    container.Children.Add(element);
            }, "SafeAddToContainer");
        }
    }
}