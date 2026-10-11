using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ScreenFerry.Core;

namespace ScreenFerry.App;

/// <summary>Checks GitHub Releases for a signed update, installs it and restarts (ADR 0009).</summary>
internal sealed class Updater : IDisposable
{
    private static readonly Uri s_manifest = new("https://github.com/ali-caglar/ScreenFerry/releases/latest/download/windows-update.json");

    // SubjectPublicKeyInfo of the update key; the private key is the WINDOWS_UPDATE_KEY secret.
    private static readonly byte[] s_publicKey = Convert.FromBase64String(
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEx+topPVrV/gyDFfT7eJTPM9f8iIxM0Gst/emlsDgKPplxeHff6k/yPZPp/XWecbpIT64BxyVv9Lk75fnapPTOw==");

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly Func<bool> _isBusy;
    private readonly Action<string> _notify;
    private readonly Action _restart;
    private readonly Timer? _timer;
    private int _running;

    /// <param name="notify">Called on a background thread.</param>
    /// <param name="restart">Called on a background thread after the new exe started.</param>
    public Updater(Func<bool> isBusy, Action<string> notify, Action restart)
    {
        _isBusy = isBusy;
        _notify = notify;
        _restart = restart;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ScreenFerry/{CurrentVersion}");
        if (IsPublished)
        {
            _timer = new Timer(_ => _ = CheckAsync(manual: false), null, TimeSpan.FromMinutes(1), TimeSpan.FromDays(1));
        }
    }

    public static string CurrentVersion =>
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>A single-file publish, the only kind that can replace itself; not a build run from the IDE.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SingleFile", "IL3000", Justification = "An empty location is exactly what identifies a single-file app.")]
    public static bool IsPublished => string.IsNullOrEmpty(typeof(Updater).Assembly.Location);

    /// <summary>Deletes the exe an update replaced.</summary>
    public static void CleanUp()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && File.Exists(exe + ".old"))
            {
                File.Delete(exe + ".old");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _http.Dispose();
    }

    public async Task CheckAsync(bool manual)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }
        try
        {
            if (!IsPublished || Environment.ProcessPath is not { } exe)
            {
                if (manual)
                {
                    _notify("Only a downloaded release of ScreenFerry can update itself.");
                }
                return;
            }
            var manifest = await _http.GetFromJsonAsync<UpdateManifest>(s_manifest).ConfigureAwait(false)
                ?? throw new JsonException("Empty update manifest.");
            if (!UpdatePolicy.IsNewer(manifest.Version, CurrentVersion))
            {
                if (manual)
                {
                    _notify($"ScreenFerry {CurrentVersion} is up to date.");
                }
                return;
            }
            var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            if (!manifest.Assets.TryGetValue(rid, out var asset))
            {
                return;
            }
            var zip = await _http.GetByteArrayAsync(asset.Url).ConfigureAwait(false);
            if (!UpdatePolicy.Verify(zip, asset, s_publicKey))
            {
                _notify($"The download of ScreenFerry {manifest.Version} failed verification, so it wasn't installed.");
                return;
            }

            var staged = exe + ".new";
            using (var archive = new ZipArchive(new MemoryStream(zip)))
            {
                var entry = archive.GetEntry("ScreenFerry.exe") ?? throw new InvalidDataException("The update has no ScreenFerry.exe.");
                entry.ExtractToFile(staged, overwrite: true);
            }
            while (_isBusy())
            {
                await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            File.Move(exe, exe + ".old", overwrite: true);
            File.Move(staged, exe);
            Process.Start(new ProcessStartInfo(exe, ["--updated", manifest.Version, "--after", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)]) { UseShellExecute = false });
            _restart();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException
            or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (manual)
            {
                _notify($"Couldn't update ScreenFerry: {e.Message}");
            }
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
