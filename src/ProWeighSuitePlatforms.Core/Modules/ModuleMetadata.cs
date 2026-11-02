using System;
using System.Collections.Generic;

namespace ProWeighSuitePlatforms.Core.Modules;

public sealed record ModuleMetadata(
    string ModuleId,
    string DisplayName,
    Version Version,
    Version MinCoreVersion,
    Version? MaxCoreVersion = null,
    IReadOnlyList<string>? DependsOn = null,
    string? RequiredLicense = null,
    string? DefaultRouteId = null,
    int Order = 0);
