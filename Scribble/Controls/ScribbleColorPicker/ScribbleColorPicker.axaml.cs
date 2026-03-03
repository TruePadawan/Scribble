using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace Scribble.Controls.ScribbleColorPicker;

/// <summary>
/// Custom control for picking a color
/// It provides a button that opens a flyout with a programmable set of colors that can be selected
/// </summary>
public partial class ScribbleColorPicker : UserControl
{
    private static readonly IReadOnlyList<Color> DefaultPalette = GenerateMaterialPalette();

    private static List<Color> GenerateMaterialPalette()
    {
        var palette = new MaterialHalfColorPalette();
        var colors = new List<Color>();

        // Loop through every base color (columns) and every shade (rows)
        for (int colorIndex = 0; colorIndex < palette.ColorCount; colorIndex++)
        {
            for (int shadeIndex = 0; shadeIndex < palette.ShadeCount; shadeIndex++)
            {
                colors.Add(palette.GetColor(colorIndex, shadeIndex));
            }
        }

        return colors;
    }

    public static readonly StyledProperty<IEnumerable<Color>> PaletteColorsProperty =
        AvaloniaProperty.Register<ScribbleColorPicker, IEnumerable<Color>>(
            nameof(PaletteColors),
            defaultValue: DefaultPalette);

    /// <summary>
    /// Property representing the colors that can be selected
    /// </summary>
    public IEnumerable<Color> PaletteColors
    {
        get => GetValue(PaletteColorsProperty);
        set => SetValue(PaletteColorsProperty, value);
    }

    public static readonly StyledProperty<Color> SelectedColorProperty =
        AvaloniaProperty.Register<ScribbleColorPicker, Color>(
            nameof(SelectedColor),
            Colors.White,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Property representing the currently selected color
    /// </summary>
    public Color SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    // To prevent infinite loop from the textbox updating the color + the color pallet updating the textbox
    private bool _isUpdatingHex = false;

    public ScribbleColorPicker()
    {
        InitializeComponent();
    }

    // Update the textbox to show the hex code of the currently selected color
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedColorProperty)
        {
            UpdateHexTextBox(change.GetNewValue<Color>());
        }
    }

    private void UpdateHexTextBox(Color color)
    {
        if (_isUpdatingHex) return;

        _isUpdatingHex = true;
        HexTextBox.Text = color.ToString().TrimStart('#').ToUpper();
        _isUpdatingHex = false;
    }

    // Update the selected color when the inputted text is a valid hex code
    private void HexTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingHex) return;

        var hex = HexTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(hex)) return;

        if (!hex.StartsWith('#')) hex = "#" + hex;

        if (Color.TryParse(hex, out Color parsedColor))
        {
            _isUpdatingHex = true;
            SelectedColor = parsedColor;
            _isUpdatingHex = false;
        }
    }

    // Close the flyout when a color is selected
    private void ColorListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0)
        {
            ColorPickerButton.Flyout?.Hide();
        }
    }
}