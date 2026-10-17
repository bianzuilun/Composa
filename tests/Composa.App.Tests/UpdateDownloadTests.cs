using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Composa.App.Tests;

/// <summary>Every download here is answered by <see cref="FakeHttp"/>: nothing reaches the network.</summary>
public sealed class UpdateDownloadTests : IDisposable
{
    private const string Base = "https://github.com/bianzuilun/Composa/releases/download/v1.3.0/";
    private const string Name = "composa_1.3.0_amd64.deb";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "composa-download-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] package = RandomNumberGenerator.GetBytes(300_000);

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private ReleaseAsset Asset => new(Name, Base + Name, package.Length);

    private ReleaseInfo Release(bool withSums = true) => new("v1.3.0", "https://github.com/bianzuilun/Composa/releases/tag/v1.3.0", false,
        withSums ? [Asset, new ReleaseAsset("sha256sums.txt", Base + "sha256sums.txt", 200)] : [Asset]);

    private Dictionary<string, byte[]> Files(string? sums = null) => new()
    {
        [Base + Name] = package,
        [Base + "sha256sums.txt"] = Encoding.UTF8.GetBytes(sums ?? $"{Sha([1, 2, 3])}  composa-1.3.0-linux-x64.tar.gz\n{Sha(package)}  {Name}\n"),
    };

    private string[] Leftovers() => Directory.Exists(folder) ? Directory.GetFiles(folder).Select(f => Path.GetFileName(f)).ToArray() : [];

    /// <summary>Reports arrive on the download's thread and are read on the test's.</summary>
    private sealed class Recorder : IProgress<DownloadProgress>
    {
        private readonly List<DownloadProgress> reports = [];
        public List<DownloadProgress> Reports { get { lock (reports) return [.. reports]; } }
        public void Report(DownloadProgress value) { lock (reports) reports.Add(value); }
    }

    [Fact]
    public async Task A_download_arrives_checked_under_its_own_name()
    {
        var progress = new Recorder();
        var http = new FakeHttp(Files());
        var result = await new UpdateDownload(http).Run(Release(), Asset, folder, progress, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Downloaded, result.Outcome);
        Assert.Equal(Path.Combine(folder, Name), result.Path);
        Assert.Equal(package, File.ReadAllBytes(result.Path!));
        Assert.Equal([Name], Leftovers()); // No .part left beside it.

        // Progress climbs to the whole file, and knows the whole from the start.
        Assert.Equal(new DownloadProgress(0, package.Length), progress.Reports[0]);
        Assert.Equal(new DownloadProgress(package.Length, package.Length), progress.Reports[^1]);
        Assert.True(progress.Reports.Count > 2);
        Assert.True(progress.Reports.Zip(progress.Reports.Skip(1)).All(p => p.First.Received <= p.Second.Received));

        // The same bare User-Agent as the check, for both requests.
        Assert.All(http.Requests, r => Assert.Equal("Composa", string.Join(" ", r.Headers.UserAgent.Select(u => u.ToString()))));
    }

    [Fact]
    public async Task A_file_that_does_not_match_its_checksum_is_thrown_away()
    {
        var files = Files(sums: $"{Sha([9, 9, 9])}  {Name}\n");
        var result = await new UpdateDownload(new FakeHttp(files)).Run(Release(), Asset, folder, null, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Failed, result.Outcome);
        Assert.Contains("did not match", result.Problem);
        Assert.Empty(Leftovers());
    }

    [Fact]
    public async Task A_release_without_checksums_downloads_nothing()
    {
        var http = new FakeHttp(Files());
        var result = await new UpdateDownload(http).Run(Release(withSums: false), Asset, folder, null, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Failed, result.Outcome);
        Assert.Contains("no list of checksums", result.Problem);
        Assert.Empty(http.Requests);
        Assert.Empty(Leftovers());
    }

    [Fact]
    public async Task Checksums_that_do_not_list_the_file_download_nothing()
    {
        var http = new FakeHttp(Files(sums: $"{Sha(package)}  some-other-file.deb\n"));
        var result = await new UpdateDownload(http).Run(Release(), Asset, folder, null, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Failed, result.Outcome);
        Assert.Contains("does not mention " + Name, result.Problem);
        Assert.Single(http.Requests); // Only the checksums were fetched.
    }

    /// <summary>Pressing Download a second time, or after downloading from the release page, fetches nothing.</summary>
    [Fact]
    public async Task A_file_already_there_with_the_right_contents_is_reused()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, Name), package);
        var http = new FakeHttp(Files());
        var result = await new UpdateDownload(http).Run(Release(), Asset, folder, null, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Reused, result.Outcome);
        Assert.Equal(Path.Combine(folder, Name), result.Path);
        Assert.Equal([Base + "sha256sums.txt"], http.Requests.Select(r => r.RequestUri!.ToString()));
    }

    /// <summary>A file of the same name with other contents is the person's own and is never overwritten.</summary>
    [Fact]
    public async Task A_different_file_of_the_same_name_is_left_alone()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, Name), [1, 2, 3]);
        var sameSize = new byte[package.Length]; // Same length, other contents: the hash has to decide.
        File.WriteAllBytes(Path.Combine(folder, "composa_1.3.0_amd64 (1).deb"), sameSize);

        var result = await new UpdateDownload(new FakeHttp(Files())).Run(Release(), Asset, folder, null, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Downloaded, result.Outcome);
        Assert.Equal(Path.Combine(folder, "composa_1.3.0_amd64 (2).deb"), result.Path);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(folder, Name)));
        Assert.Equal(sameSize, File.ReadAllBytes(Path.Combine(folder, "composa_1.3.0_amd64 (1).deb")));
        Assert.Equal(package, File.ReadAllBytes(result.Path!));
    }

    [Theory]
    [InlineData("composa_1.3.0_amd64.deb", "composa_1.3.0_amd64 (1).deb")]
    [InlineData("composa-1.3.0-linux-x64.tar.gz", "composa-1.3.0-linux-x64 (1).tar.gz")]
    [InlineData("Composa-1.3.0-x86_64.AppImage", "Composa-1.3.0-x86_64 (1).AppImage")]
    [InlineData("composa-1.3.0-win-x64-setup.exe", "composa-1.3.0-win-x64-setup (1).exe")]
    public void A_numbered_copy_keeps_its_extension(string name, string expected) => Assert.Equal(expected, UpdateDownload.Numbered(name, 1));

    [Fact]
    public async Task Cancelling_stops_and_leaves_no_part_file()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var http = new FakeHttp((request, _) => Task.FromResult(request.RequestUri!.ToString().EndsWith("sha256sums.txt")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Files()[Base + "sha256sums.txt"]) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream(package[..70_000])) }));
        var progress = new Recorder();
        var download = new UpdateDownload(http).Run(Release(), Asset, folder, progress, cancel.Token);

        // Wait for the first bytes to be written, so the cancel lands mid-download with a .part on disk.
        for (var i = 0; i < 200 && !progress.Reports.Any(p => p.Received > 0); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Contains(Name + ".part", Leftovers());
        await cancel.CancelAsync();
        var result = await download;

        Assert.Equal(DownloadOutcome.Cancelled, result.Outcome);
        Assert.Empty(Leftovers());
    }

    /// <summary>A second Composa fetching the same file cannot open the first one's partial file, and so never deletes it.</summary>
    [Fact]
    public async Task A_second_download_of_the_same_file_leaves_the_first_ones_part_alone()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var http = new FakeHttp((request, _) => Task.FromResult(request.RequestUri!.ToString().EndsWith("sha256sums.txt")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Files()[Base + "sha256sums.txt"]) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream(package[..70_000])) }));
        var progress = new Recorder();
        var first = new UpdateDownload(http).Run(Release(), Asset, folder, progress, cancel.Token);
        for (var i = 0; i < 200 && !progress.Reports.Any(p => p.Received > 0); i++) await Task.Delay(10, TestContext.Current.CancellationToken);

        var second = await new UpdateDownload(new FakeHttp(Files())).Run(Release(), Asset, folder, null, TestContext.Current.CancellationToken);
        Assert.Equal(DownloadOutcome.Failed, second.Outcome);
        Assert.Equal([Name + ".part"], Leftovers());
        Assert.True(new FileInfo(Path.Combine(folder, Name + ".part")).Length > 0); // Not truncated by the second attempt either.

        await cancel.CancelAsync();
        Assert.Equal(DownloadOutcome.Cancelled, (await first).Outcome);
        Assert.Empty(Leftovers());
    }

    /// <summary>No limit on the whole download, but a connection that goes quiet fails instead of hanging for ever.</summary>
    [Fact]
    public async Task A_connection_that_stops_delivering_fails()
    {
        var http = new FakeHttp((request, _) => Task.FromResult(request.RequestUri!.ToString().EndsWith("sha256sums.txt")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Files()[Base + "sha256sums.txt"]) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream(package[..70_000])) }));
        var result = await new UpdateDownload(http, stallAfter: TimeSpan.FromMilliseconds(300)).Run(Release(), Asset, folder, null, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadOutcome.Failed, result.Outcome);
        Assert.Contains("stalled", result.Problem);
        Assert.Empty(Leftovers());
    }

    /// <summary>A server that never answers at all is a stall too.</summary>
    [Fact]
    public async Task A_server_that_never_answers_fails()
    {
        var http = new FakeHttp(async (_, cancel) =>
        {
            await Task.Delay(Timeout.Infinite, cancel);
            throw new InvalidOperationException("unreachable");
        });
        var result = await new UpdateDownload(http, stallAfter: TimeSpan.FromMilliseconds(200)).Run(Release(), Asset, folder, null, TestContext.Current.CancellationToken);
        Assert.Equal(DownloadOutcome.Failed, result.Outcome);
        Assert.Contains("stalled", result.Problem);
    }

    [Fact]
    public async Task Being_offline_is_said_in_words()
    {
        var http = new FakeHttp((_, _) => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known."));
        var result = await new UpdateDownload(http).Run(Release(), Asset, folder, null, TestContext.Current.CancellationToken);
        Assert.Equal(DownloadOutcome.Failed, result.Outcome);
        Assert.Equal("Could not reach GitHub. Check the connection and try again.", result.Problem);
    }

    [Fact]
    public async Task A_file_gone_from_the_release_is_said_in_words()
    {
        var files = Files();
        files.Remove(Base + Name);
        var result = await new UpdateDownload(new FakeHttp(files)).Run(Release(), Asset, folder, null, TestContext.Current.CancellationToken);
        Assert.Equal(DownloadOutcome.Failed, result.Outcome);
        Assert.Contains("no longer has this file", result.Problem);
        Assert.Empty(Leftovers());
    }

    /// <summary>A folder that cannot be created (here because a file already has its name) is reported, not thrown.</summary>
    [Fact]
    public async Task A_folder_that_cannot_be_written_is_said_in_words()
    {
        File.WriteAllBytes(folder, [0]);
        try
        {
            var result = await new UpdateDownload(new FakeHttp(Files())).Run(Release(), Asset, Path.Combine(folder, "Downloads"), null, TestContext.Current.CancellationToken);
            Assert.Equal(DownloadOutcome.Failed, result.Outcome);
            Assert.Contains(Path.Combine(folder, "Downloads"), result.Problem);
        }
        finally { File.Delete(folder); }
    }

    [Fact]
    public void A_full_disk_is_said_in_words()
    {
        var full = new IOException("No space left on device", OperatingSystem.IsWindows() ? unchecked((int)0x80070070) : 28);
        Assert.Equal("There is not enough space in /home/ada/Downloads for the download.", UpdateDownload.Describe(full, "/home/ada/Downloads"));
    }

    [Fact]
    public async Task A_file_only_the_release_page_should_offer_is_refused()
    {
        var elsewhere = new ReleaseAsset(Name, "https://example.com/" + Name, 1);
        var http = new FakeHttp(Files());
        var result = await new UpdateDownload(http).Run(Release(), elsewhere, folder, null, TestContext.Current.CancellationToken);
        Assert.Equal(DownloadOutcome.Failed, result.Outcome);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void Checksums_read_both_of_sha256sums_modes()
    {
        var text = $"{Sha([1])}  text-mode.deb\r\n{Sha([2]).ToUpperInvariant()} *binary-mode.rpm\nnot a line\n\n{new string('z', 64)}  not-hex.zip\n";
        var sums = Checksums.Parse(text);
        Assert.Equal(2, sums.Count);
        Assert.Equal(Sha([1]), sums["text-mode.deb"]);
        Assert.Equal(Sha([2]), sums["binary-mode.rpm"]);
    }

    /// <summary>Delivers some bytes and then nothing, until it is cancelled.</summary>
    private sealed class HangingStream(byte[] first) : Stream
    {
        private int position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            if (position < first.Length)
            {
                var count = Math.Min(buffer.Length, first.Length - position);
                first.AsMemory(position, count).CopyTo(buffer);
                position += count;
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
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
