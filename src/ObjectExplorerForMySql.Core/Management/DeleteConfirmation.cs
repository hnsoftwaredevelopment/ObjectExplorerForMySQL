using System;

namespace ObjectExplorerForMySql.Core.Management;

/// <summary>State of the Delete confirmation dialog (US-0901).</summary>
public sealed class DeleteConfirmation : ObservableObject
{
    private string _typedName = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="DeleteConfirmation"/> class.</summary>
    public DeleteConfirmation(DropTarget target)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>Gets the object to delete.</summary>
    public DropTarget Target { get; }

    /// <summary>Gets the statement that runs.</summary>
    public string Sql => Target.Sql;

    /// <summary>Gets a value indicating whether the name must be typed (a database).</summary>
    public bool RequiresName => Target.RequiresNameConfirmation;

    /// <summary>Gets or sets the name the user typed.</summary>
    public string TypedName
    {
        get => _typedName;
        set
        {
            if (SetProperty(ref _typedName, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanDelete));
            }
        }
    }

    /// <summary>Gets a value indicating whether the Delete button is enabled.</summary>
    public bool CanDelete => Target.IsConfirmedBy(_typedName);
}
