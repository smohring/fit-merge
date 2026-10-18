using System.IO;
using Dynastream.Fit;
using FitMerge.Models;

namespace FitMerge.Services;

/// <summary>Writes the merged messages as a valid FIT activity file.</summary>
public static class FitWriter
{
    /// <summary>
    /// Writes file_id and device_info (creator) from <paramref name="device"/>, then the merged messages.
    /// These two messages decide whether Garmin Connect recalculates Training Effect, Training Load and VO2max.
    /// </summary>
    public static void Write(string path, IEnumerable<Mesg> messages, DeviceMetadata device)
    {
        var body = messages.ToList();
        var startTime = body.OfType<RecordMesg>().First().GetTimestamp();

        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        var encoder = new Encode(ProtocolVersion.V20);
        encoder.Open(stream);

        var fileId = new FileIdMesg();
        fileId.SetType(Dynastream.Fit.File.Activity);
        fileId.SetManufacturer(device.Manufacturer);
        if (device.Product is ushort product) fileId.SetProduct(product);
        if (device.SerialNumber is uint serial) fileId.SetSerialNumber(serial);
        if (device.ProductName is string name) fileId.SetProductName(name);
        fileId.SetTimeCreated(new Dynastream.Fit.DateTime(System.DateTime.UtcNow));
        encoder.Write(fileId);

        var deviceInfo = new DeviceInfoMesg();
        deviceInfo.SetTimestamp(startTime);
        deviceInfo.SetDeviceIndex(DeviceIndex.Creator);
        deviceInfo.SetManufacturer(device.Manufacturer);
        if (device.Product is ushort p) deviceInfo.SetProduct(p);
        if (device.SerialNumber is uint sn) deviceInfo.SetSerialNumber(sn);
        if (device.SoftwareVersion is float sv) deviceInfo.SetSoftwareVersion(sv);
        if (device.ProductName is string pn) deviceInfo.SetProductName(pn);
        deviceInfo.SetSourceType(SourceType.Local);
        encoder.Write(deviceInfo);

        foreach (var mesg in body)
            encoder.Write(mesg);

        encoder.Close(); // writes header size and CRC
    }
}
