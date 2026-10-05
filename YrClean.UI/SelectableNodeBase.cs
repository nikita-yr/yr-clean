using System.ComponentModel;

namespace YrClean.UI;

// Base class giving tree nodes a checkbox-bindable IsSelected property.
// Checking a parent cascades the selection down to all its children automatically.
public abstract class SelectableNodeBase : INotifyPropertyChanged
{
    private bool _isSelected;

    protected SelectableNodeBase(string name, string sizeDisplay)
    {
        Name = name;
        SizeDisplay = sizeDisplay;
    }

    public string Name { get; }
    public string SizeDisplay { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));

            foreach (var child in GetChildren())
            {
                child.IsSelected = value;
            }
        }
    }

    protected abstract IEnumerable<SelectableNodeBase> GetChildren();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
