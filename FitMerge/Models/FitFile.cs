using Dynastream.Fit;

namespace FitMerge.Models;

/// <summary>A parsed FIT file: raw messages plus the figures shown in the preview.</summary>
public sealed class FitFile
{
    public required string Path { get; init; }
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Data points sorted by ascending timestamp.</summary>
    public required List<RecordMesg> Records { get; init; }
    public required List<LapMesg> Laps { get; init; }
    public SessionMesg? Session { get; init; }

    public required DeviceMetadata Device { get; init; }
    /// <summary>Raw metadata messages, used for the verification output.</summary>
    public FileIdMesg? FileId { get; init; }
    public DeviceInfoMesg? Creator { get; init; }

    public Sport Sport { get; init; }
    public SubSport SubSport { get; init; }
    public System.DateTime Start { get; init; }   // UTC
    public System.DateTime End { get; init; }     // UTC
    public TimeSpan Elapsed => End - Start;
    public double DistanceMeters { get; init; }
    public double TimerSeconds { get; init; }
    public int? AvgHeartRate { get; init; }
    public int? MaxHeartRate { get; init; }
    public int? Calories { get; init; }
    /// <summary>Number of recording gaps longer than <see cref="Services.FitReader.PauseGapSeconds"/>.</summary>
    public int PauseCount { get; init; }

    /// <summary>List entry text, e.g. "7/27/2026 9:15 AM · 1:23:45 · 12.3 km".</summary>
    public string Summary =>
        $"{Start.ToLocalTime():g} · {Elapsed:h\\:mm\\:ss}" +
        (DistanceMeters > 0 ? $" · {DistanceMeters / 1000:0.0} km" : "");

    public override string ToString() => FileName;
}
