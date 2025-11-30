using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Scribble.Behaviours;

public class ToggleButtonGroup
{
    public static readonly AttachedProperty<string?> GroupNameProperty =
        AvaloniaProperty.RegisterAttached<ToggleButtonGroup, ToggleButton, string?>("GroupName");

    public static string? GetGroupName(ToggleButton control) => control.GetValue(GroupNameProperty);
    public static void SetGroupName(ToggleButton control, string? value) => control.SetValue(GroupNameProperty, value);

    static ToggleButtonGroup()
    {
        GroupNameProperty.Changed.AddClassHandler<ToggleButton>(OnGroupNameChanged);
    }

    // Event subscription manager
    private static void OnGroupNameChanged(ToggleButton toggleButton, AvaloniaPropertyChangedEventArgs args)
    {
        toggleButton.IsCheckedChanged -= ToggleButton_Checked;
        if (args.NewValue is string)
        {
            toggleButton.IsCheckedChanged += ToggleButton_Checked;
        }
    }

    // Uncheck all other ToggleButton in the group
    private static void ToggleButton_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton checkedButton)
        {
            var groupName = GetGroupName(checkedButton);
            if (string.IsNullOrEmpty(groupName)) return;

            // Prevent unchecking
            if (checkedButton.IsChecked == false)
            {
                checkedButton.IsCheckedChanged -= ToggleButton_Checked;
                checkedButton.IsChecked = true;
                checkedButton.IsCheckedChanged += ToggleButton_Checked;
                return;
            }

            var parent = checkedButton.Parent;
            if (parent is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is ToggleButton otherButton && child != checkedButton &&
                        GetGroupName(otherButton) == groupName && otherButton.IsChecked == true)
                    {
                        otherButton.IsCheckedChanged -= ToggleButton_Checked;
                        otherButton.IsChecked = false;
                        otherButton.IsCheckedChanged += ToggleButton_Checked;
                    }
                }
            }
        }

    }
}