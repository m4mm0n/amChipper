using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using amChipper.App.ViewModels;
using amChipper.Core.Models;

namespace amChipper.App.Controls.Automation;

/// <summary>
/// Represents the ClipEnvelopeControl component.
/// </summary>
public partial class ClipEnvelopeControl : UserControl
{
    /// <summary>
    /// Stores or exposes _vm.
    /// </summary>
    private ClipEnvelopeViewModel? _vm;

    public ClipEnvelopeControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
    }

    /// <summary>
    /// Executes the OnDataContextChanged operation.
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as ClipEnvelopeViewModel;
        Lane.ViewModel = _vm;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;

        Refresh();
    }

    /// <summary>
    /// Executes the OnViewModelPropertyChanged operation.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => Lane.InvalidateVisual();

    /// <summary>
    /// Executes the Refresh operation.
    /// </summary>
    private void Refresh()
    {
        _vm?.Refresh();
        Lane.InvalidateVisual();
    }

    /// <summary>
    /// Executes the SetVolume operation.
    /// </summary>
    private void SetVolume(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Target = AutomationTarget.Volume;
        BtnPan.IsChecked = false;
    }

    /// <summary>
    /// Executes the SetPan operation.
    /// </summary>
    private void SetPan(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Target = AutomationTarget.Pan;
        BtnVol.IsChecked = false;
    }
}
