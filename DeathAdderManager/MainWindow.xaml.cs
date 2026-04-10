using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DeathAdderManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}

public class NavButton : RadioButton
{
    public NavButton()
    {
        Loaded += (_, _) => SyncCheckedState();
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register("Icon", typeof(string), typeof(NavButton));
        
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register("Label", typeof(string), typeof(NavButton));
        
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register("SelectedIndex", typeof(int), typeof(NavButton),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
    
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        SelectedIndex = TabIndex;
    }
    
    // We update our checked state based on SelectedIndex
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == SelectedIndexProperty || e.Property == TabIndexProperty)
        {
            SyncCheckedState();
        }
    }

    private void SyncCheckedState()
    {
        var shouldBeChecked = SelectedIndex == TabIndex;
        if (IsChecked != shouldBeChecked)
        {
            IsChecked = shouldBeChecked;
        }
    }
}

