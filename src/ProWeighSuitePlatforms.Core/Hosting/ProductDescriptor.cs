using System;

namespace ProWeighSuitePlatforms.Core.Hosting;

/// <summary>
/// Identifies the product composed by a host. Used by Info and licensing only;
/// it must never be used to select or filter product modules at runtime.
/// </summary>
public sealed record ProductDescriptor(
    string Id,
    string DisplayName,
    Version Version,
    string? LicenseKey = null);
