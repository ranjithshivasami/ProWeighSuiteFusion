using System;
using System.Collections.Generic;

namespace ProWeighSuitePlatforms.Core.Modules;

public sealed class ModuleValidationException : Exception
{
    public ModuleValidationException(IReadOnlyList<string> errors)
        : base("Module registration validation failed: " + string.Join("; ", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
