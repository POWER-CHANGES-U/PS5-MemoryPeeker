using System.Windows;

namespace PS5MemoryPeeker;

public sealed class ManualAddressWindow : Window
{
    private readonly System.Windows.Controls.TextBox _addressBox = new() { Text = "0x0", MinWidth = 220 };
    private readonly System.Windows.Controls.ComboBox _typeBox = new() { ItemsSource = MemoryValueCodec.ValueTypes, SelectedIndex = 2, MinWidth = 140 };
    private readonly System.Windows.Controls.TextBox _valueBox = new() { Text = "0", MinWidth = 220 };
    private readonly System.Windows.Controls.TextBox _descriptionBox = new() { Text = "Manual value", MinWidth = 220 };

    public ManualAddressWindow()
    {
        Title = "New Address";
        Width = 420;
        Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["AppFont"];

        System.Windows.Controls.Grid grid = new() { Margin = new Thickness(18) };
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 5; i++)
        {
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        }

        AddRow(grid, 0, "Address", _addressBox);
        AddRow(grid, 1, "Type", _typeBox);
        AddRow(grid, 2, "Value", _valueBox);
        AddRow(grid, 3, "Description", _descriptionBox);

        System.Windows.Controls.StackPanel buttons = new() { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        System.Windows.Controls.Button add = new() { Content = "Add", MinWidth = 86, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        System.Windows.Controls.Button cancel = new() { Content = "Cancel", MinWidth = 86, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        add.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        System.Windows.Controls.Grid.SetRow(buttons, 4);
        System.Windows.Controls.Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);

        Content = grid;
    }

    public CheatRow ToCheatRow()
    {
        return new CheatRow
        {
            AddressText = _addressBox.Text,
            TypeText = (string)_typeBox.SelectedItem,
            Value = _valueBox.Text,
            Description = _descriptionBox.Text
        };
    }

    private static void AddRow(System.Windows.Controls.Grid grid, int row, string label, FrameworkElement input)
    {
        System.Windows.Controls.TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 10) };
        input.Margin = new Thickness(0, 0, 0, 10);
        System.Windows.Controls.Grid.SetRow(text, row);
        System.Windows.Controls.Grid.SetColumn(text, 0);
        System.Windows.Controls.Grid.SetRow(input, row);
        System.Windows.Controls.Grid.SetColumn(input, 1);
        grid.Children.Add(text);
        grid.Children.Add(input);
    }
}
