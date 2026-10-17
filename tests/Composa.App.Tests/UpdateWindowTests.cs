using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Composa.Editing;
using SkiaSharp;

namespace Composa.App.Tests;

/// <summary>
/// The update strip in the window, from Help &gt; Check for Updates to Install. The releases, the
/// network, the Downloads folder and every program an install would start are stand-ins, so
/// nothing reaches GitHub and nothing is started.
/// </summary>
public sealed class UpdateWindowTests : IDisposable
{
    // Newer than anything this build can be, without comparing against the build's own version.
    private const string Tag = "v99.0.0";
    private const string Base = "https://github.com/bianzuilun/Composa/releases/download/" + Tag + "/";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "composa-update-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] package = RandomNumberGenerator.GetBytes(200_000);

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static readonly string[] Names =
    [
        "composa_99.0.0_amd64.deb", "composa_99.0.0_arm64.deb", "composa-99.0.0-1.x86_64.rpm", "composa-99.0.0-1.aarch64.rpm",
        "Composa-99.0.0-x86_64.AppImage", "Composa-99.0.0-aarch64.AppImage", "composa-99.0.0-linux-x64.tar.gz", "composa-99.0.0-linux-arm64.tar.gz",
        "composa-99.0.0-win-x64-setup.exe", "composa-99.0.0-win-arm64-setup.exe", "composa-99.0.0-win-x64.zip", "composa-99.0.0-win-arm64.zip",
    ];

    private sealed class Source(ReleaseInfo release) : IReleaseSource
    {
        public Task<ReleaseInfo?> Latest(CancellationToken cancel) => Task.FromResult<ReleaseInfo?>(release);
    }

    private ReleaseInfo Release() => new(Tag, "https://github.com/bianzuilun/Composa/releases/tag/" + Tag, false,
        [.. Names.Select(n => new ReleaseAsset(n, Base + n, package.Length)), new ReleaseAsset("sha256sums.txt", Base + "sha256sums.txt", 1000)]);

    /// <summary>Every package has the same bytes here, which is all a checksum can tell apart.</summary>
    private FakeHttp Files()
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(package));
        var files = Names.ToDictionary(n => Base + n, _ => package);
        files[Base + "sha256sums.txt"] = Encoding.UTF8.GetBytes(string.Concat(Names.Select(n => $"{sha}  {n}\n")));
        return new FakeHttp(files);
    }

    private sealed class Programs
    {
        public List<ShellCommand> Ran { get; } = [];
        public List<string> Opened { get; } = [];
        public List<string> Revealed { get; } = [];
    }

    private (MainWindow Window, Programs Programs) Open(InstallKind kind, HttpMessageHandler? http = null)
    {
        var programs = new Programs();
        var window = new MainWindow
        {
            Width = 1280,
            Height = 800,
            Updates = new UpdateEnvironment
            {
                Source = new Source(Release()),
                Http = http ?? Files(),
                Folder = () => folder,
                Kind = () => kind,
                Run = command => { programs.Ran.Add(command); return true; },
                Open = path => { programs.Opened.Add(path); return Task.FromResult(true); },
                Reveal = path => { programs.Revealed.Add(path); return Task.FromResult(true); },
            }
        };
        window.Show();
        return (window, programs);
    }

    /// <summary>The file this processor's install of a kind gets, so the tests pass on arm64 as on x64.</summary>
    private string Expected(InstallKind kind) => Path.Combine(folder, ReleaseAssets.For(kind, RuntimeInformation.ProcessArchitecture, Release())!.Name);

    private static void PumpUntil(Func<bool> done)
    {
        for (var i = 0; i < 500 && !done(); i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Assert.True(done(), "timed out waiting");
    }

    private static void CheckForUpdates(MainWindow window)
    {
        var item = window.GetLogicalDescendants().OfType<Menu>().First().GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "Check for Updates…");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        PumpUntil(() => window.UpdateNotice.State == UpdateNotice.Phase.Available);
    }

    [AvaloniaFact]
    public void Checking_by_hand_offers_the_download()
    {
        var (window, _) = Open(InstallKind.Deb);
        CheckForUpdates(window);
        Assert.Equal(["Download", "Release notes", "Skip this version"], window.UpdateNotice.Buttons);
        Assert.True(Screenshots.Save(window, "20-update-window-available"));
    }

    [AvaloniaFact]
    public void A_developer_build_is_offered_the_release_notes_only()
    {
        var (window, _) = Open(InstallKind.Developer);
        CheckForUpdates(window);
        Assert.Equal(["Release notes", "Skip this version"], window.UpdateNotice.Buttons);
    }

    [AvaloniaFact]
    public void A_downloaded_package_is_opened_with_the_software_installer()
    {
        var (window, programs) = Open(InstallKind.Deb);
        CheckForUpdates(window);
        window.UpdateNotice.Press("Download");
        PumpUntil(() => window.UpdateNotice.State == UpdateNotice.Phase.Ready);

        var path = Expected(InstallKind.Deb);
        Assert.True(File.Exists(path));
        Assert.Equal(package, File.ReadAllBytes(path));
        Assert.Equal(["Show in Folder", "Install"], window.UpdateNotice.Buttons);
        Assert.Equal("sudo apt install " + Installer.ShellQuote(path), window.UpdateNotice.Command);
        Assert.True(Screenshots.Save(window, "20-update-window-ready"));

        window.UpdateNotice.Press("Show in Folder");
        PumpUntil(() => programs.Revealed.Count == 1);
        Assert.Equal([path], programs.Revealed);

        window.UpdateNotice.Press("Install");
        PumpUntil(() => programs.Opened.Count == 1);
        Assert.Equal([path], programs.Opened);
        Assert.Empty(programs.Ran);
        Assert.True(window.IsVisible); // A package manager replaces files under a running program; nothing quits.
    }

    /// <summary>The setup replaces files Composa holds open, so Install quits first, asking about unsaved work as Quit does.</summary>
    [AvaloniaFact]
    public void Installing_on_Windows_asks_about_unsaved_work_then_quits_and_starts_the_setup()
    {
        var (window, programs) = Open(InstallKind.WindowsInstaller);
        var document = EditorSession.NewCanvas(64, 48, SKColors.White);
        window.AddSession(document);
        document.MarkModified();

        CheckForUpdates(window);
        window.UpdateNotice.Press("Download");
        PumpUntil(() => window.UpdateNotice.State == UpdateNotice.Phase.Ready);
        Assert.Equal(["Show in Folder", "Install"], window.UpdateNotice.Buttons);
        Assert.Null(window.UpdateNotice.Command);

        // The unsaved document asks; Cancel there stops the install as it would stop a quit.
        window.UpdateNotice.Press("Install");
        var prompt = WaitForDialog(window);
        Press(prompt, "Cancel");
        PumpUntil(() => !prompt.IsVisible);
        Assert.Empty(programs.Ran);
        Assert.True(window.IsVisible);

        window.UpdateNotice.Press("Install");
        prompt = WaitForDialog(window);
        Press(prompt, "Don't Save");
        PumpUntil(() => !window.IsVisible);
        var setup = Assert.Single(programs.Ran);
        Assert.Equal(Expected(InstallKind.WindowsInstaller), setup.Program);
        Assert.True(setup.Shell);
    }

    /// <summary>Text being typed is not a change until it is committed; Install commits it first, so the unsaved text is asked about.</summary>
    [AvaloniaFact]
    public void Installing_while_typing_asks_about_the_text()
    {
        var (window, programs) = Open(InstallKind.WindowsInstaller);
        var document = EditorSession.NewCanvas(200, 100, SKColors.White);
        window.AddSession(document);
        document.BeginText(new SKPoint(20, 50)).Insert("Unsaved words");
        Assert.True(document.IsEditingText);

        CheckForUpdates(window);
        window.UpdateNotice.Press("Download");
        PumpUntil(() => window.UpdateNotice.State == UpdateNotice.Phase.Ready);
        window.UpdateNotice.Press("Install");
        var prompt = WaitForDialog(window);
        Press(prompt, "Cancel");
        PumpUntil(() => !prompt.IsVisible);

        Assert.Empty(programs.Ran);
        Assert.True(window.IsVisible);
        Assert.False(document.IsEditingText);
        Assert.True(document.IsModified);
    }

    /// <summary>An error that is neither the network's nor the disk's still ends the download, rather than leaving the strip in Downloading with nothing to cancel.</summary>
    [AvaloniaFact]
    public void An_unexpected_error_ends_the_download_in_words()
    {
        var http = new FakeHttp((_, _) => throw new InvalidOperationException("Something nobody expected"));
        var (window, _) = Open(InstallKind.Deb, http);
        CheckForUpdates(window);
        window.UpdateNotice.Press("Download");
        PumpUntil(() => window.UpdateNotice.State == UpdateNotice.Phase.Failed);
        Assert.Contains("Something nobody expected", window.UpdateNotice.Message);
        Assert.Equal(["Retry", "Release notes"], window.UpdateNotice.Buttons);
    }

    /// <summary>Cancelling at the unsaved-changes prompt cancels the quit, and a download under way carries on.</summary>
    [AvaloniaFact]
    public void A_quit_cancelled_at_the_prompt_leaves_the_download_running()
    {
        var files = Files();
        var http = new FakeHttp(async (request, cancel) =>
            request.RequestUri!.ToString().EndsWith("sha256sums.txt")
                ? await new HttpMessageInvoker(files).SendAsync(request, cancel)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Trickle(package, () => { })) });
        var (window, _) = Open(InstallKind.Tarball, http);
        var document = EditorSession.NewCanvas(64, 48, SKColors.White);
        window.AddSession(document);
        document.MarkModified();
        CheckForUpdates(window);
        window.UpdateNotice.Press("Download");
        PumpUntil(() => Directory.Exists(folder) && Directory.GetFiles(folder).Any(f => f.EndsWith(".part")));

        window.Close();
        var prompt = WaitForDialog(window);
        Press(prompt, "Cancel");
        PumpUntil(() => !prompt.IsVisible);
        Assert.True(window.IsVisible);
        Assert.Equal(UpdateNotice.Phase.Downloading, window.UpdateNotice.State);
        Assert.Contains(Directory.GetFiles(folder), f => f.EndsWith(".part"));

        window.UpdateNotice.Press("Cancel");
        PumpUntil(() => Directory.GetFiles(folder).Length == 0);
    }

    [AvaloniaFact]
    public void A_downloaded_AppImage_is_executable_and_has_no_Install()
    {
        var (window, _) = Open(InstallKind.AppImage);
        CheckForUpdates(window);
        window.UpdateNotice.Press("Download");
        PumpUntil(() => window.UpdateNotice.State == UpdateNotice.Phase.Ready);

        Assert.Equal(["Show in Folder"], window.UpdateNotice.Buttons);
        Assert.Null(window.UpdateNotice.Command);
        if (!OperatingSystem.IsWindows())
        {
            var path = Directory.GetFiles(folder).Single();
            Assert.True(File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute));
        }
    }

    /// <summary>A failure says why in the strip, and Retry goes again.</summary>
    [AvaloniaFact]
    public void A_failed_download_can_be_tried_again()
    {
        var files = Files();
        var fail = true;
        var http = new FakeHttp(async (request, cancel) => fail
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused")
            : await new HttpMessageInvoker(files).SendAsync(request, cancel));
        var (window, _) = Open(InstallKind.Tarball, http);
        CheckForUpdates(window);
        window.UpdateNotice.Press("Download");
        PumpUntil(() => window.UpdateNotice.State == UpdateNotice.Phase.Failed);
        Assert.Equal("Could not reach GitHub. Check the connection and try again.", window.UpdateNotice.Message);

        fail = false;
        window.UpdateNotice.Press("Retry");
        PumpUntil(() => window.UpdateNotice.State == UpdateNotice.Phase.Ready);
        Assert.Single(Directory.GetFiles(folder));
    }

    /// <summary>Closing mid-download cancels it and waits for the partial file to go, so none is left in Downloads.</summary>
    [AvaloniaFact]
    public void Closing_the_window_during_a_download_leaves_no_part_file()
    {
        var files = Files();
        var started = false;
        var http = new FakeHttp(async (request, cancel) =>
        {
            if (request.RequestUri!.ToString().EndsWith("sha256sums.txt")) return await new HttpMessageInvoker(files).SendAsync(request, cancel);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Trickle(package, () => started = true)) };
        });
        var (window, _) = Open(InstallKind.Rpm, http);
        CheckForUpdates(window);
        window.UpdateNotice.Press("Download");
        PumpUntil(() => started && Directory.Exists(folder) && Directory.GetFiles(folder).Any(f => f.EndsWith(".part")));
        Assert.Equal(UpdateNotice.Phase.Downloading, window.UpdateNotice.State);

        window.Close();
        PumpUntil(() => !window.IsVisible);
        Assert.Empty(Directory.GetFiles(folder));
    }

    /// <summary>Cancel puts the strip back to Download.</summary>
    [AvaloniaFact]
    public void Cancelling_a_download_offers_it_again()
    {
        var files = Files();
        var http = new FakeHttp(async (request, cancel) =>
            request.RequestUri!.ToString().EndsWith("sha256sums.txt")
                ? await new HttpMessageInvoker(files).SendAsync(request, cancel)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Trickle(package, () => { })) });
        var (window, _) = Open(InstallKind.WindowsZip, http);
        CheckForUpdates(window);
        window.UpdateNotice.Press("Download");
        PumpUntil(() => window.UpdateNotice.State == UpdateNotice.Phase.Downloading && Directory.Exists(folder) && Directory.GetFiles(folder).Length > 0);
        window.UpdateNotice.Press("Cancel");
        PumpUntil(() => window.UpdateNotice.State == UpdateNotice.Phase.Available);
        Assert.Contains("Download", window.UpdateNotice.Buttons);
        PumpUntil(() => Directory.GetFiles(folder).Length == 0);
    }

    private static Window WaitForDialog(MainWindow owner)
    {
        Window? dialog = null;
        PumpUntil(() => (dialog = owner.OwnedWindows.FirstOrDefault(w => w.IsVisible)) != null);
        return dialog!;
    }

    private static void Press(Window dialog, string label)
    {
        var button = dialog.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == label);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Delivers a first piece at once and the rest never, until cancelled: a download that is always under way.</summary>
    private sealed class Trickle(byte[] bytes, Action started) : Stream
    {
        private bool sent;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            if (!sent)
            {
                sent = true;
                started();
                var count = Math.Min(buffer.Length, bytes.Length / 4);
                bytes.AsMemory(0, count).CopyTo(buffer);
                return count;
            }
            await Task.Delay(Timeout.Infinite, cancel);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel) => ReadAsync(buffer.AsMemory(offset, count), cancel).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
