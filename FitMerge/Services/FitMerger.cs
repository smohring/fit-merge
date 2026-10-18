using Dynastream.Fit;
using FitMerge.Models;

namespace FitMerge.Services;

/// <summary>How overlapping data points of several files are resolved.</summary>
public enum OverlapMode
{
    /// <summary>Keep only the earlier file's points inside the overlap.</summary>
    PreferFirst,
    /// <summary>Keep only the later file's points inside the overlap.</summary>
    PreferSecond,
    /// <summary>Keep all points; on identical timestamps the earlier file wins.</summary>
    KeepAll,
}

public sealed class MergeOptions
{
    public OverlapMode Overlap { get; set; } = OverlapMode.PreferFirst;
    /// <summary>Write gaps longer than 60 s as timer stop/start events.</summary>
    public bool MarkGapsAsPause { get; set; } = true;
    /// <summary>Product ID replacing the first file's (implies manufacturer garmin).</summary>
    public ushort? DeviceProduct { get; set; }
    /// <summary>Serial number replacing the first file's.</summary>
    public uint? DeviceSerial { get; set; }
}

/// <summary>What an analysis finding is about; also selects the text via the resource key "Issue&lt;Kind&gt;".</summary>
public enum IssueKind
{
    Sports, Duplicate, Overlap, Gap, Pause,
    DeviceInherited, DeviceOverridden, NotGarmin, NoProduct, NoSerial, OtherDevice,
}

public sealed record MergeIssue(IssueKind Kind, bool IsWarning, string Text)
{
    public string Icon => IsWarning ? "⚠" : "ℹ";
}

/// <summary>Detects conflicts between files and builds the merged message list.</summary>
public static class FitMerger
{
    /// <summary>Device identity of the target file: the first file in the list, overridden by the options.</summary>
    public static DeviceMetadata ResolveDevice(IReadOnlyList<FitFile> files, MergeOptions options) =>
        files[0].Device.WithOverrides(options.DeviceProduct, options.DeviceSerial);

    /// <summary>Finds overlaps, gaps, pauses, sport conflicts and device-metadata problems.</summary>
    public static List<MergeIssue> Analyze(IReadOnlyList<FitFile> files, MergeOptions? options = null)
    {
        var issues = new List<MergeIssue>(AnalyzeDevice(files, options ?? new MergeOptions()));
        var sorted = files.OrderBy(f => f.Start).ToList();

        var sports = sorted.Select(f => f.Sport).Distinct().ToList();
        if (sports.Count > 1)
            issues.Add(Issue(IssueKind.Sports, true, string.Join(", ", sports)));

        for (int i = 1; i < sorted.Count; i++)
        {
            var prev = sorted[i - 1];
            var cur = sorted[i];
            if (cur.Start == prev.Start && cur.End == prev.End)
                issues.Add(Issue(IssueKind.Duplicate, true, prev.FileName, cur.FileName));
            else if (cur.Start < prev.End)
                issues.Add(Issue(IssueKind.Overlap, true, Fmt(prev.End - cur.Start), prev.FileName, cur.FileName));
            else if ((cur.Start - prev.End).TotalSeconds > FitReader.PauseGapSeconds)
                issues.Add(Issue(IssueKind.Gap, false, Fmt(cur.Start - prev.End), prev.FileName, cur.FileName));
        }

        foreach (var f in sorted.Where(f => f.PauseCount > 0))
            issues.Add(Issue(IssueKind.Pause, false, f.FileName, f.PauseCount));

        return issues;
    }

    /// <summary>Checks that the target device identity lets Garmin Connect recalculate (manufacturer Garmin, product, serial).</summary>
    private static IEnumerable<MergeIssue> AnalyzeDevice(IReadOnlyList<FitFile> files, MergeOptions options)
    {
        var device = ResolveDevice(files, options);
        var source = files[0];

        yield return options.DeviceProduct is null && options.DeviceSerial is null
            ? Issue(IssueKind.DeviceInherited, false, source.FileName, device.Display)
            : Issue(IssueKind.DeviceOverridden, false, device.Display);

        if (!device.IsGarmin)
            yield return Issue(IssueKind.NotGarmin, true, device.ManufacturerDisplay);
        else if (device.Product is null)
            yield return Issue(IssueKind.NoProduct, true);

        if (device.SerialNumber is null)
            yield return Issue(IssueKind.NoSerial, true);

        var others = files.Skip(1)
            .Where(f => f.Device.SerialNumber is not null && f.Device.SerialNumber != source.Device.SerialNumber)
            .ToList();
        if (others.Count > 0)
            yield return Issue(IssueKind.OtherDevice, false, string.Join(", ", others.Select(f => f.Device.Display).Distinct()));
    }

    /// <summary>Merges the files into FIT messages in write order (without file_id and device_info).</summary>
    public static List<Mesg> Merge(IReadOnlyList<FitFile> files, MergeOptions options)
    {
        var sorted = files.OrderBy(f => f.Start).ToList();
        var records = BuildTimeline(sorted, options.Overlap);

        uint startTs = FitReader.Timestamp(records[0]);
        uint endTs = FitReader.Timestamp(records[^1]);

        var body = new List<Mesg> { TimerEvent(startTs, EventType.Start) };
        double pausedSeconds = 0;
        for (int i = 0; i < records.Count; i++)
        {
            uint gap = i > 0 ? FitReader.Timestamp(records[i]) - FitReader.Timestamp(records[i - 1]) : 0;
            if (options.MarkGapsAsPause && gap > FitReader.PauseGapSeconds)
            {
                body.Add(TimerEvent(FitReader.Timestamp(records[i - 1]), EventType.StopAll));
                body.Add(TimerEvent(FitReader.Timestamp(records[i]), EventType.Start));
                pausedSeconds += gap;
            }
            body.Add(records[i]);
        }
        body.Add(TimerEvent(endTs, EventType.StopAll));

        var laps = sorted.SelectMany(f => f.Laps.Select(l => new LapMesg(l)))
            .OrderBy(l => l.GetStartTime()?.GetTimeStamp() ?? 0)
            .ToList();
        for (int i = 0; i < laps.Count; i++)
            laps[i].SetMessageIndex((ushort)i);
        body.AddRange(laps);

        float elapsed = endTs - startTs;
        float timer = (float)(elapsed - pausedSeconds);
        var heartRates = records.Select(r => r.GetHeartRate()).Where(hr => hr is > 0).Select(hr => (int)hr!).ToList();
        var first = sorted[0];

        var session = new SessionMesg();
        session.SetMessageIndex(0);
        session.SetStartTime(new Dynastream.Fit.DateTime(startTs));
        session.SetTimestamp(new Dynastream.Fit.DateTime(endTs));
        session.SetTotalElapsedTime(elapsed);
        session.SetTotalTimerTime(timer);
        session.SetSport(first.Sport);
        session.SetSubSport(first.SubSport);
        session.SetEvent(Event.Session);
        session.SetEventType(EventType.Stop);
        session.SetFirstLapIndex(0);
        session.SetNumLaps((ushort)laps.Count);
        var totalDistance = records.LastOrDefault(r => r.GetDistance() != null)?.GetDistance();
        if (totalDistance != null) session.SetTotalDistance(totalDistance.Value);
        var calories = sorted.Sum(f => f.Calories ?? 0);
        if (calories > 0) session.SetTotalCalories((ushort)Math.Min(calories, ushort.MaxValue));
        if (heartRates.Count > 0)
        {
            session.SetAvgHeartRate((byte)heartRates.Average());
            session.SetMaxHeartRate((byte)heartRates.Max());
        }
        body.Add(session);

        var activity = new ActivityMesg();
        activity.SetTimestamp(new Dynastream.Fit.DateTime(endTs));
        activity.SetNumSessions(1);
        activity.SetType(Activity.Manual);
        activity.SetEvent(Event.Activity);
        activity.SetEventType(EventType.Stop);
        activity.SetTotalTimerTime(timer);
        body.Add(activity);

        return body;
    }

    /// <summary>
    /// Combines all files' data points on one timeline according to <paramref name="mode"/>.
    /// Works on copies and adds a running offset so the per-file cumulative distance stays continuous.
    /// </summary>
    private static List<RecordMesg> BuildTimeline(List<FitFile> sorted, OverlapMode mode)
    {
        var timeline = new List<RecordMesg>();
        double distanceOffset = 0;
        foreach (var file in sorted)
        {
            IEnumerable<RecordMesg> incoming = file.Records;
            if (timeline.Count > 0)
            {
                uint lastKept = FitReader.Timestamp(timeline[^1]);
                uint fileStart = FitReader.Timestamp(file.Records[0]);
                switch (mode)
                {
                    case OverlapMode.PreferFirst:
                        incoming = file.Records.Where(r => FitReader.Timestamp(r) > lastKept);
                        break;
                    case OverlapMode.PreferSecond:
                        timeline.RemoveAll(r => FitReader.Timestamp(r) >= fileStart);
                        break;
                    case OverlapMode.KeepAll:
                        var taken = timeline.Select(FitReader.Timestamp).ToHashSet();
                        incoming = file.Records.Where(r => !taken.Contains(FitReader.Timestamp(r)));
                        break;
                }
            }

            double? lastDistanceInFile = null;
            foreach (var original in incoming)
            {
                var copy = new RecordMesg(original);
                var d = copy.GetDistance();
                if (d != null)
                {
                    lastDistanceInFile = d.Value;
                    copy.SetDistance((float)(distanceOffset + d.Value));
                }
                timeline.Add(copy);
            }
            distanceOffset += lastDistanceInFile ?? file.DistanceMeters;
        }
        timeline.Sort((a, b) => FitReader.Timestamp(a).CompareTo(FitReader.Timestamp(b)));
        return timeline;
    }

    private static EventMesg TimerEvent(uint timestamp, EventType type)
    {
        var e = new EventMesg();
        e.SetTimestamp(new Dynastream.Fit.DateTime(timestamp));
        e.SetEvent(Event.Timer);
        e.SetEventType(type);
        e.SetTimerTrigger((byte)TimerTrigger.Manual);
        return e;
    }

    private static MergeIssue Issue(IssueKind kind, bool isWarning, params object[] args) =>
        new(kind, isWarning, Loc.Get($"Issue{kind}", args));

    private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") + " h" : t.ToString(@"m\:ss") + " min";
}
