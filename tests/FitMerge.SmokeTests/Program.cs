// End-to-end smoke tests: synthesize FIT files, read, analyze, merge, read back.
using System.IO;
using Dynastream.Fit;
using FitMerge.Models;
using FitMerge.Services;

Loc.SetLanguage("en");
string dir = Path.Combine(Path.GetTempPath(), "fitmerge-test");
Directory.CreateDirectory(dir);
int failures = 0;

// --- Fixtures ---------------------------------------------------------------
// a, b: Edge 1040 (SN 3312345678). c: other serial. d: Wahoo. e: no device info.
var t0 = new System.DateTime(2026, 7, 26, 8, 0, 0, System.DateTimeKind.Utc);
string fileA = MakeActivity(Path.Combine(dir, "a.fit"), t0, 600, 140, 2.5f);                               // 08:00-08:10
string fileB = MakeActivity(Path.Combine(dir, "b.fit"), t0.AddMinutes(15), 600, 150, 3.0f);                 // 08:15-08:25 (5 min gap)
string fileC = MakeActivity(Path.Combine(dir, "c.fit"), t0.AddMinutes(5), 600, 160, 2.0f, serial: 9999);    // 08:05-08:15 (overlaps a)
string fileD = MakeActivity(Path.Combine(dir, "d.fit"), t0, 600, 140, 2.5f, manufacturer: Manufacturer.WahooFitness, product: 1);
string fileE = MakeActivity(Path.Combine(dir, "e.fit"), t0, 600, 140, 2.5f, writeDevice: false);

var a = FitReader.Read(fileA);
var b = FitReader.Read(fileB);
var c = FitReader.Read(fileC);
var d = FitReader.Read(fileD);
var e = FitReader.Read(fileE);

// --- Reading ----------------------------------------------------------------
Check("A: 601 records", a.Records.Count == 601);
Check("A: distance ~1500 m", Math.Abs(a.DistanceMeters - 1500) < 5);
Check("A: heart rate 140", a.AvgHeartRate == 140);
Check("A: sport running", a.Sport == Sport.Running);
Check("A: no pauses", a.PauseCount == 0);

// --- Device metadata reading ------------------------------------------------
Check("A: manufacturer Garmin", a.Device.IsGarmin);
Check("A: product Edge1040", a.Device.Product == GarminProduct.Edge1040);
Check("A: serial 3312345678", a.Device.SerialNumber == 3312345678);
Check("A: software version 9.25", a.Device.SoftwareVersion == 9.25f);
Check("A: display text", a.Device.Display == "Garmin Edge1040 · SN 3312345678");
Check("D: Wahoo is not Garmin", !d.Device.IsGarmin);
Check("E: no device info -> unknown", e.Device.Product is null && e.Device.SerialNumber is null);

// --- Product names come from the SDK profile --------------------------------
Check("Resolve 'edge_1040'", GarminProducts.Resolve("edge_1040") == GarminProduct.Edge1040);
Check("Resolve 'Edge1040'", GarminProducts.Resolve("Edge1040") == GarminProduct.Edge1040);
Check("Resolve 'fenix_7'", GarminProducts.Resolve("fenix_7") == GarminProduct.Fenix7);
Check("Resolve numeric '3843'", GarminProducts.Resolve("3843") == GarminProduct.Edge1040);
Check("NameOf(Edge1040)", GarminProducts.NameOf(GarminProduct.Edge1040) == "Edge1040");
Check("AllNames lists > 400 devices", GarminProducts.AllNames.Count > 400);
try
{
    GarminProducts.Resolve("edge_9999");
    Check("unknown device throws", false);
}
catch (ArgumentException ex)
{
    Check("unknown device throws", true);
    Check("error message lists valid names", ex.Message.Contains("Edge1040") && ex.Message.Contains("FIT SDK profile"));
    Console.WriteLine("      -> " + ex.Message.Split('\n')[0]);
}

// --- Analysis (asserted by kind, not by wording) ----------------------------
Check("A+B: gap detected", FitMerger.Analyze([a, b]).Any(i => i.Kind == IssueKind.Gap && !i.IsWarning));
Check("A+C: overlap detected", FitMerger.Analyze([a, c]).Any(i => i.Kind == IssueKind.Overlap && i.IsWarning));
Check("A+A: duplicate detected", FitMerger.Analyze([a, FitReader.Read(fileA)]).Any(i => i.Kind == IssueKind.Duplicate));
Check("D+B: not-Garmin warning", FitMerger.Analyze([d, b]).Any(i => i.Kind == IssueKind.NotGarmin && i.IsWarning));
Check("E+B: missing-serial warning", FitMerger.Analyze([e, b]).Any(i => i.Kind == IssueKind.NoSerial && i.IsWarning));
Check("A+C: other-device hint", FitMerger.Analyze([a, c]).Any(i => i.Kind == IssueKind.OtherDevice));

// --- Merge with a gap (A + B) -----------------------------------------------
var ab = MergeTo("merged_ab.fit", [a, b], new MergeOptions());
Check("A+B: 1202 records", ab.Records.Count == 1202);
Check("A+B: distance ~3300 m", Math.Abs(ab.DistanceMeters - 3300) < 10);
Check("A+B: elapsed 25 min", Math.Abs(ab.Elapsed.TotalMinutes - 25) < 0.1);
Check("A+B: timer ~20 min (pause subtracted)", Math.Abs(ab.TimerSeconds - 1200) < 15);
Check("A+B: 1 pause in target", ab.PauseCount == 1);
Check("A+B: 2 laps", ab.Laps.Count == 2);
Check("A+B: session present", ab.Session != null);

// --- Default: metadata of the FIRST input file ------------------------------
Check("A+B: file_id present", ab.FileId != null);
Check("A+B: file_id type=activity", ab.FileId!.GetType() == Dynastream.Fit.File.Activity);
Check("A+B: file_id manufacturer=garmin", ab.FileId.GetManufacturer() == Manufacturer.Garmin);
Check("A+B: file_id product=Edge1040", ab.FileId.GetProduct() == GarminProduct.Edge1040);
Check("A+B: file_id serial inherited", ab.FileId.GetSerialNumber() == 3312345678);
Check("A+B: file_id time_created set", ab.FileId.GetTimeCreated() != null);
Check("A+B: device_info present", ab.Creator != null);
Check("A+B: device_info device_index=0 (creator)", ab.Creator!.GetDeviceIndex() == DeviceIndex.Creator);
Check("A+B: device_info manufacturer=garmin", ab.Creator.GetManufacturer() == Manufacturer.Garmin);
Check("A+B: device_info product=Edge1040", ab.Creator.GetProduct() == GarminProduct.Edge1040);
Check("A+B: device_info serial inherited", ab.Creator.GetSerialNumber() == 3312345678);
Check("A+B: device_info software_version inherited", ab.Creator.GetSoftwareVersion() == 9.25f);
Check("A+B: device_info source_type=local", ab.Creator.GetSourceType() == SourceType.Local);

// --- Override: --device without --serial keeps the serial -------------------
var dev = MergeTo("merged_device.fit", [a, b], new MergeOptions { DeviceProduct = GarminProducts.Resolve("fenix_7") });
Check("--device: product=Fenix7", dev.FileId!.GetProduct() == GarminProduct.Fenix7);
Check("--device without --serial keeps input serial", dev.FileId.GetSerialNumber() == 3312345678);
Check("--device: device_info also Fenix7", dev.Creator!.GetProduct() == GarminProduct.Fenix7);

// --- Override: --serial -----------------------------------------------------
var ser = MergeTo("merged_serial.fit", [a, b], new MergeOptions { DeviceSerial = 4242424242 });
Check("--serial: file_id serial set", ser.FileId!.GetSerialNumber() == 4242424242);
Check("--serial: device_info serial set", ser.Creator!.GetSerialNumber() == 4242424242);
Check("--serial alone keeps product", ser.FileId.GetProduct() == GarminProduct.Edge1040);

// --- Non-Garmin source ------------------------------------------------------
var wahoo = MergeTo("merged_wahoo.fit", [d, b], new MergeOptions { DeviceProduct = GarminProducts.Resolve("edge_1040") });
Check("Wahoo source + --device: manufacturer=garmin", wahoo.FileId!.GetManufacturer() == Manufacturer.Garmin);
var wahooPlain = MergeTo("merged_wahoo_plain.fit", [d, b], new MergeOptions());
Check("Wahoo source without override keeps Wahoo", wahooPlain.FileId!.GetManufacturer() == Manufacturer.WahooFitness);

// --- Merge with overlap (A + C) ---------------------------------------------
var ac = MergeTo("merged_ac.fit", [a, c], new MergeOptions { Overlap = OverlapMode.PreferFirst });
Check("A+C PreferFirst: 901 records", ac.Records.Count == 901);
Check("A+C: distance monotonic", IsMonotonic(ac));
var ac2 = MergeTo("merged_ac2.fit", [a, c], new MergeOptions { Overlap = OverlapMode.PreferSecond });
Check("A+C PreferSecond: 901 records", ac2.Records.Count == 901);
var ac3 = MergeTo("merged_ac3.fit", [a, c], new MergeOptions { Overlap = OverlapMode.KeepAll });
Check("A+C KeepAll: 901 records (duplicates removed)", ac3.Records.Count == 901);

// --- Merge without pause marking --------------------------------------------
var np = MergeTo("merged_nopause.fit", [a, b], new MergeOptions { MarkGapsAsPause = false });
Check("A+B without pause option: timer = 25 min", Math.Abs(np.TimerSeconds - 1500) < 15);

// --- Integrity of the target file (SDK CRC check) ---------------------------
using (var fs = System.IO.File.OpenRead(Path.Combine(dir, "merged_ab.fit")))
    Check("A+B: SDK integrity check passed", new Decode().CheckIntegrity(fs));

// --- Localization -----------------------------------------------------------
foreach (var lang in new[] { "en", "de" })
{
    Loc.SetLanguage(lang);
    var missing = Enum.GetValues<IssueKind>().Where(k => Loc.Get($"Issue{k}", "x", "y", "z") == $"Issue{k}").ToList();
    Check($"[{lang}] every IssueKind has a text", missing.Count == 0);
    Check($"[{lang}] analysis texts are filled in", FitMerger.Analyze([a, b]).All(i => !i.Text.Contains("{0}")));
}
Loc.SetLanguage("de");
Check("German text differs from English", Loc.Get("StatusNone") == "Füge mindestens zwei FIT-Dateien hinzu.");
Check("German unknown-device message", Throws(() => GarminProducts.Resolve("edge_9999"), "Unbekanntes Gerät"));
Loc.SetLanguage("en");
Check("English text restored", Loc.Get("StatusNone") == "Add at least two FIT files.");

// --- Verification output ----------------------------------------------------
Console.WriteLine("\n=== Verification: target file read back ===");
Console.WriteLine("[Default - metadata from first input file]");
Console.WriteLine(FitReader.DescribeMetadata(ab));
Console.WriteLine("\n[--device fenix_7 (without --serial)]");
Console.WriteLine(FitReader.DescribeMetadata(dev));
Console.WriteLine("\n[--serial 4242424242]");
Console.WriteLine(FitReader.DescribeMetadata(ser));

Console.WriteLine(failures == 0 ? "\nAll tests passed." : $"\n{failures} test(s) failed!");
return failures;

FitFile MergeTo(string name, List<FitFile> files, MergeOptions options)
{
    string path = Path.Combine(dir, name);
    FitWriter.Write(path, FitMerger.Merge(files, options), FitMerger.ResolveDevice(files, options));
    return FitReader.Read(path);
}

void Check(string name, bool ok)
{
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
}

static bool Throws(Action action, string messagePart)
{
    try { action(); return false; }
    catch (ArgumentException ex) { return ex.Message.Contains(messagePart); }
}

static bool IsMonotonic(FitFile f)
{
    float last = -1;
    foreach (var r in f.Records)
    {
        var d = r.GetDistance();
        if (d == null) continue;
        if (d.Value < last) return false;
        last = d.Value;
    }
    return true;
}

// One record per second, constant heart rate and speed, one lap.
static string MakeActivity(string path, System.DateTime startUtc, int durationSec, byte hr, float speed,
    uint serial = 3312345678, ushort manufacturer = Manufacturer.Garmin, ushort product = GarminProduct.Edge1040,
    bool writeDevice = true)
{
    using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
    var enc = new Encode(ProtocolVersion.V20);
    enc.Open(fs);

    var start = new Dynastream.Fit.DateTime(startUtc);
    var end = new Dynastream.Fit.DateTime(startUtc.AddSeconds(durationSec));

    var fileId = new FileIdMesg();
    fileId.SetType(Dynastream.Fit.File.Activity);
    fileId.SetTimeCreated(start);
    if (writeDevice)
    {
        fileId.SetManufacturer(manufacturer);
        fileId.SetProduct(product);
        fileId.SetSerialNumber(serial);
    }
    enc.Write(fileId);

    if (writeDevice)
    {
        var info = new DeviceInfoMesg();
        info.SetTimestamp(start);
        info.SetDeviceIndex(DeviceIndex.Creator);
        info.SetManufacturer(manufacturer);
        info.SetProduct(product);
        info.SetSerialNumber(serial);
        info.SetSoftwareVersion(9.25f);
        info.SetSourceType(SourceType.Local);
        enc.Write(info);
    }

    var ev = new EventMesg();
    ev.SetTimestamp(start);
    ev.SetEvent(Event.Timer);
    ev.SetEventType(EventType.Start);
    enc.Write(ev);

    for (int s = 0; s <= durationSec; s++)
    {
        var r = new RecordMesg();
        r.SetTimestamp(new Dynastream.Fit.DateTime(startUtc.AddSeconds(s)));
        r.SetHeartRate(hr);
        r.SetSpeed(speed);
        r.SetDistance(s * speed);
        enc.Write(r);
    }

    var stop = new EventMesg();
    stop.SetTimestamp(end);
    stop.SetEvent(Event.Timer);
    stop.SetEventType(EventType.StopAll);
    enc.Write(stop);

    var lap = new LapMesg();
    lap.SetMessageIndex(0);
    lap.SetStartTime(start);
    lap.SetTimestamp(end);
    lap.SetTotalElapsedTime(durationSec);
    lap.SetTotalTimerTime(durationSec);
    lap.SetTotalDistance(durationSec * speed);
    enc.Write(lap);

    var session = new SessionMesg();
    session.SetMessageIndex(0);
    session.SetStartTime(start);
    session.SetTimestamp(end);
    session.SetTotalElapsedTime(durationSec);
    session.SetTotalTimerTime(durationSec);
    session.SetSport(Sport.Running);
    session.SetTotalDistance(durationSec * speed);
    session.SetTotalCalories(100);
    session.SetFirstLapIndex(0);
    session.SetNumLaps(1);
    enc.Write(session);

    var activity = new ActivityMesg();
    activity.SetTimestamp(end);
    activity.SetNumSessions(1);
    enc.Write(activity);

    enc.Close();
    return path;
}
