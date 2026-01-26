namespace PZTools.Core.Models.InputDialog
{
    public sealed class InputFieldDefinition
    {
        public required string Key { get; init; }

        public required string Label { get; init; }

        public string? Description { get; init; }

        public string? Placeholder { get; init; }

        public string? DefaultValue { get; init; }

        public bool IsRequired { get; init; } = false;

        public bool IsPassword { get; init; } = false;

        public Func<string?, bool>? Validator { get; init; }

        public string? ValidationMessage { get; init; }
    }
}
