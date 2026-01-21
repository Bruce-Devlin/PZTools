namespace PZTools.Core.Models.InputDialog
{
    public sealed class InputFieldDefinition
    {
        /// <summary>Unique key used to read the result back (e.g. "projectName").</summary>
        public required string Key { get; init; }

        /// <summary>UI label shown above the input.</summary>
        public required string Label { get; init; }

        /// <summary>Optional hint text shown under label.</summary>
        public string? Description { get; init; }

        /// <summary>Optional placeholder text (watermark-style).</summary>
        public string? Placeholder { get; init; }

        public string? DefaultValue { get; init; }

        public bool IsRequired { get; init; } = false;

        /// <summary>If true, this will be a password-style input.</summary>
        public bool IsPassword { get; init; } = false;

        /// <summary>Optional: validate the field. Return true if valid.</summary>
        public Func<string?, bool>? Validator { get; init; }

        public string? ValidationMessage { get; init; }
    }
}
