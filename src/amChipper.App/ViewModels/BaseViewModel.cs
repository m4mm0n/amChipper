using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace amChipper.App.ViewModels;

/// <summary>Base class providing INotifyPropertyChanged for all ViewModels.</summary>
public abstract class BaseViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Raised when PropertyChangedEventHandler changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Executes the OnPropertyChanged operation.
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Executes the SetField operation.
    /// </summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
