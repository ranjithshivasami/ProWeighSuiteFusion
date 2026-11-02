using System.ComponentModel;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.ViewModels
{
    /// <summary>
    /// A display item for the searchable Material Code dropdown. Wraps a <see cref="Material"/>
    /// so the list can also carry a non-selectable "No matches found." placeholder and a flag
    /// used to highlight the item whose code exactly matches the typed text.
    /// </summary>
    public class MaterialSuggestion : INotifyPropertyChanged
    {
        public Material? Material { get; }
        public string ProductCode { get; }
        public string ProductName { get; }
        public bool IsActive { get; }
        public bool IsPlaceholder { get; }

        private bool _isExactMatch;
        public bool IsExactMatch
        {
            get => _isExactMatch;
            set
            {
                if (_isExactMatch == value) return;
                _isExactMatch = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExactMatch)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private MaterialSuggestion(Material? material, string code, string name, bool isActive, bool isPlaceholder)
        {
            Material = material;
            ProductCode = code;
            ProductName = name;
            IsActive = isActive;
            IsPlaceholder = isPlaceholder;
        }

        public MaterialSuggestion(Material material)
            : this(material, material.ProductCode ?? string.Empty, material.ProductName ?? string.Empty,
                   material.IsActive, isPlaceholder: false)
        {
        }

        public static MaterialSuggestion CreatePlaceholder() =>
            new(null, "No matches found.", string.Empty, isActive: false, isPlaceholder: true);

        public override string ToString() => ProductCode;
    }
}
