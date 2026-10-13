using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ScreenFerry.Core;

namespace ScreenFerry.App;

/// <summary>Checks GitHub Releases for a signed update and runs its installer, which restarts the app (ADR 0009, 0010).</summary>
internal sealed class Updater : IDisposable
{
    private static readonly Uri s_manifest = new("https://github.com/ali-caglar/ScreenFerry/releases/latest/download/windows-update.json");

    // SubjectPublicKeyInfo of the update key; the private key is the WINDOWS_UPDATE_KEY secret.
    private static readonly byte[] s_publicKey = Convert.FromBase64String(
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEx+topPVrV/gyDFfT7eJTPM9f8iIxM0Gst/emlsDgKPplxeHff6k/yPZPp/XWecbpIT64BxyVv9Lk75fnapPTOw==");

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly Func<bool> _isBusy;
    private readonly Action<string> _notify;
    private readonly Action _quit;
    private readonly Timer? _timer;
    private int _running;

    /// <param name="notify">Called on a background thread.</param>
    /// <param name="quit">Called on a background thread after the installer started.</param>
    public Updater(Func<bool> isBusy, Action<string> notify, Action quit)
    {
        _isBusy = isBusy;
        _notify = notify;
        _quit = quit;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ScreenFerry/{CurrentVersion}");
        if (IsPublished)
        {
            _timer = new Timer(_ => _ = CheckAsync(manual: false), null, TimeSpan.FromMinutes(1), TimeSpan.FromDays(1));
        }
    }

    public static string CurrentVersion =>
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>A single-file publish, as installed; not a build run from the IDE.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SingleFile", "IL3000", Justification = "An empty location is exactly what identifies a single-file app.")]
    public static bool IsPublished => string.IsNullOrEmpty(typeof(Updater).Assembly.Location);

    private static string SetupPath => Path.Combine(Path.GetTempPath(), "ScreenFerry-setup.exe");

    /// <summary>Deletes the installer of the last update.</summary>
    public static void CleanUp()
    {
        try
        {
            File.Delete(SetupPath);
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
            if (!IsPublished)
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
            var setup = await _http.GetByteArrayAsync(asset.Url).ConfigureAwait(false);
            if (!UpdatePolicy.Verify(setup, asset, s_publicKey))
            {
                _notify($"The download of ScreenFerry {manifest.Version} failed verification, so it wasn't installed.");
                return;
            }

            await File.WriteAllBytesAsync(SetupPath, setup).ConfigureAwait(false);
            while (_isBusy())
            {
                await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            // The installer waits for this process to quit, then starts the new version.
            Process.Start(new ProcessStartInfo(SetupPath, ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", $"/UPDATED={manifest.Version}"]) { UseShellExecute = false });
            _quit();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
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
