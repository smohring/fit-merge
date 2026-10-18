using System.Globalization;
using System.IO;
using Dynastream.Fit;
using FitMerge.Models;

namespace FitMerge.Services;

/// <summary>Reads a FIT file with the Garmin SDK and computes the preview figures.</summary>
public static class FitReader
{
    /// <summary>Gaps between data points longer than this count as a pause.</summary>
    public const int PauseGapSeconds = 60;

    public static FitFile Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = new Decode();
        var listener = new FitListener();
        decoder.MesgEvent += listener.OnMesg;

        if (!decoder.IsFIT(stream))
            throw new InvalidDataException(Loc.Get("ErrNotFit"));
        stream.Position = 0;
        decoder.Read(stream); // returns false on CRC errors; messages decoded so far stay usable

        var messages = listener.FitMessages;
        var records = messages.RecordMesgs
            .Where(r => r.GetTimestamp() != null)
            .OrderBy(Timestamp)
            .ToList();
        if (records.Count == 0)
            throw new InvalidDataException(Loc.Get("ErrNoRecords"));

        var session = messages.SessionMesgs.FirstOrDefault();
        var heartRates = records.Select(r => r.GetHeartRate()).Where(hr => hr is > 0).Select(hr => (int)hr!).ToList();

        int pauses = 0;
        for (int i = 1; i < records.Count; i++)
            if (Timestamp(records[i]) - Timestamp(records[i - 1]) > PauseGapSeconds)
                pauses++;

        var start = records[0].GetTimestamp().GetDateTime();
        var end = records[^1].GetTimestamp().GetDateTime();
        double distance = session?.GetTotalDistance()
            ?? records.LastOrDefault(r => r.GetDistance() != null)?.GetDistance()
            ?? 0;

        return new FitFile
        {
            Path = path,
            Device = ReadDevice(messages),
            FileId = messages.FileIdMesgs.FirstOrDefault(),
            Creator = FindCreator(messages),
            Records = records,
            Laps = messages.LapMesgs.OrderBy(l => l.GetStartTime()?.GetTimeStamp() ?? 0).ToList(),
            Session = session,
            Sport = session?.GetSport() ?? messages.SportMesgs.FirstOrDefault()?.GetSport() ?? Sport.Generic,
            SubSport = session?.GetSubSport() ?? SubSport.Generic,
            Start = start,
            End = end,
            DistanceMeters = distance,
            TimerSeconds = session?.GetTotalTimerTime() ?? (end - start).TotalSeconds,
            AvgHeartRate = heartRates.Count > 0 ? (int)heartRates.Average() : null,
            MaxHeartRate = heartRates.Count > 0 ? heartRates.Max() : null,
            Calories = session?.GetTotalCalories(),
            PauseCount = pauses,
        };
    }

    public static uint Timestamp(RecordMesg record) => record.GetTimestamp().GetTimeStamp();

    /// <summary>Device identity from device_info (creator), falling back to file_id.</summary>
    private static DeviceMetadata ReadDevice(FitMessages messages)
    {
        var fileId = messages.FileIdMesgs.FirstOrDefault();
        var fromFileId = fileId is null ? null : new DeviceMetadata(
            fileId.GetManufacturer() ?? Manufacturer.Invalid,
            fileId.GetProduct(),
            fileId.GetSerialNumber(),
            null,
            fileId.GetProductNameAsString());

        var creator = FindCreator(messages);
        var fromDeviceInfo = creator is null ? null : new DeviceMetadata(
            creator.GetManufacturer() ?? Manufacturer.Invalid,
            creator.GetProduct(),
            creator.GetSerialNumber(),
            creator.GetSoftwareVersion(),
            creator.GetProductNameAsString());

        return fromDeviceInfo?.Merge(fromFileId) ?? fromFileId ?? DeviceMetadata.Unknown;
    }

    private static DeviceInfoMesg? FindCreator(FitMessages messages) =>
        messages.DeviceInfoMesgs.FirstOrDefault(d => d.GetDeviceIndex() == DeviceIndex.Creator)
        ?? messages.DeviceInfoMesgs.FirstOrDefault(d => d.GetManufacturer() != null);

    /// <summary>Lists the file_id and device_info fields actually stored in the file (language-independent protocol output).</summary>
    public static string DescribeMetadata(FitFile file)
    {
        var lines = new List<string>();
        if (file.FileId is { } id)
            lines.Add("file_id:     " + Join(
                ("type", id.GetType()?.ToString().ToLowerInvariant()),
                ("manufacturer", Name(id.GetManufacturer())),
                ("product", Product(id.GetManufacturer(), id.GetProduct())),
                ("serial_number", id.GetSerialNumber()?.ToString()),
                ("time_created", id.GetTimeCreated()?.GetDateTime().ToString("u"))));
        else
            lines.Add("file_id:     missing");

        if (file.Creator is { } dev)
            lines.Add("device_info: " + Join(
                ("device_index", dev.GetDeviceIndex() is byte i ? (i == DeviceIndex.Creator ? "0 (creator)" : i.ToString()) : null),
                ("manufacturer", Name(dev.GetManufacturer())),
                ("product", Product(dev.GetManufacturer(), dev.GetProduct())),
                ("serial_number", dev.GetSerialNumber()?.ToString()),
                ("software_version", dev.GetSoftwareVersion()?.ToString(CultureInfo.InvariantCulture)),
                ("source_type", dev.GetSourceType()?.ToString().ToLowerInvariant())));
        else
            lines.Add("device_info: missing");

        return string.Join("\n", lines);

        static string Join(params (string Key, string? Value)[] fields) =>
            string.Join(", ", fields.Select(f => $"{f.Key}={f.Value ?? "–"}"));

        static string? Name(ushort? manufacturer) => manufacturer is ushort m
            ? $"{GarminProducts.ManufacturerName(m)?.ToLowerInvariant() ?? "?"} ({m})" : null;

        static string? Product(ushort? manufacturer, ushort? product) => product is ushort p
            ? (manufacturer == Manufacturer.Garmin && GarminProducts.NameOf(p) is string n ? $"{n} ({p})" : p.ToString())
            : null;
    }
}
