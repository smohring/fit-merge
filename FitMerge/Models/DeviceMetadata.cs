using Dynastream.Fit;
using FitMerge.Services;

namespace FitMerge.Models;

/// <summary>Device identity of a FIT file (file_id / device_info) that Garmin Connect uses to decide on recalculation.</summary>
public sealed record DeviceMetadata(
    ushort Manufacturer,
    ushort? Product,
    uint? SerialNumber,
    float? SoftwareVersion,
    string? ProductName)
{
    public bool IsGarmin => Manufacturer == Dynastream.Fit.Manufacturer.Garmin;

    public string ProductDisplay =>
        (IsGarmin && Product is ushort p ? GarminProducts.NameOf(p) : null)
        ?? ProductName
        ?? Product?.ToString()
        ?? Loc.Get("DeviceUnknown");

    public string ManufacturerDisplay =>
        Manufacturer == Dynastream.Fit.Manufacturer.Invalid
            ? Loc.Get("DeviceUnknown")
            : GarminProducts.ManufacturerName(Manufacturer) ?? Manufacturer.ToString();

    /// <summary>One-line summary such as "Garmin Edge1040 · SN 3312345678".</summary>
    public string Display =>
        $"{ManufacturerDisplay} {ProductDisplay} · " +
        (SerialNumber is uint sn ? Loc.Get("DeviceSerial", sn) : Loc.Get("DeviceNoSerial"));

    /// <summary>Fills fields this instance lacks from <paramref name="fallback"/>.</summary>
    public DeviceMetadata Merge(DeviceMetadata? fallback) => fallback is null ? this : new(
        Manufacturer != Dynastream.Fit.Manufacturer.Invalid ? Manufacturer : fallback.Manufacturer,
        Product ?? fallback.Product,
        SerialNumber ?? fallback.SerialNumber,
        SoftwareVersion ?? fallback.SoftwareVersion,
        ProductName ?? fallback.ProductName);

    /// <summary>Applies --device / --serial; a new product always implies manufacturer garmin.</summary>
    public DeviceMetadata WithOverrides(ushort? product, uint? serialNumber) => new(
        product is not null ? Dynastream.Fit.Manufacturer.Garmin : Manufacturer,
        product ?? Product,
        serialNumber ?? SerialNumber,
        SoftwareVersion,
        product is not null ? null : ProductName);

    /// <summary>Stand-in for a source file without any device information.</summary>
    public static DeviceMetadata Unknown { get; } =
        new(Dynastream.Fit.Manufacturer.Invalid, null, null, null, null);
}
