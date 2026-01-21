using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PZTools.Core.Models.InputDialog
{
    public sealed class InputFieldViewModel : INotifyPropertyChanged
    {
        public string Key { get; }
        public string Label { get; }
        public string? Description { get; }
        public string? Placeholder { get; }
        public bool IsRequired { get; }
        public bool IsPassword { get; }

        private string? _value;
        public string? Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsValid)); }
        }

        public Func<string?, bool>? Validator { get; }
        public string? ValidationMessage { get; }

        public bool IsValid
        {
            get
            {
                if (IsRequired && string.IsNullOrWhiteSpace(Value)) return false;
                if (Validator is null) return true;
                return Validator(Value);
            }
        }

        public InputFieldViewModel(InputFieldDefinition def)
        {
            Key = def.Key;
            Label = def.Label;
            Description = def.Description;
            Placeholder = def.Placeholder;
            IsRequired = def.IsRequired;
            IsPassword = def.IsPassword;
            Validator = def.Validator;
            ValidationMessage = def.ValidationMessage ?? "Invalid value.";
            Value = def.DefaultValue ?? string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
