using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ObjectExplorerForMySql.Core;

/// <summary>Minimal <see cref="INotifyPropertyChanged"/> base class for view models.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets <paramref name="field"/> and raises <see cref="PropertyChanged"/> when the value changes.</summary>
    /// <returns>True when the value changed.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
