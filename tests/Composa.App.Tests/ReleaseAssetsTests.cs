using System.Runtime.InteropServices;
using System.Text;

namespace Composa.App.Tests;

public class ReleaseAssetsTests
{
    /// <summary>
    /// What the GitHub API answered for v1.2.0, cut down to the fields Composa reads. The names are
    /// the packaging scripts' own, so a package renamed in <c>scripts/package</c> without the same
    /// change in <see cref="ReleaseAssets"/> fails here rather than in someone's update strip.
    /// </summary>
    public const string V120 = """
    {
      "tag_name": "v1.2.0",
      "html_url": "https://github.com/bianzuilun/Composa/releases/tag/v1.2.0",
      "prerelease": false,
      "assets": [
        { "name": "Composa-1.2.0-aarch64.AppImage", "size": 41060872, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/Composa-1.2.0-aarch64.AppImage" },
        { "name": "Composa-1.2.0-x86_64.AppImage", "size": 43510264, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/Composa-1.2.0-x86_64.AppImage" },
        { "name": "composa-1.2.0-1.aarch64.rpm", "size": 44057151, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa-1.2.0-1.aarch64.rpm" },
        { "name": "composa-1.2.0-1.x86_64.rpm", "size": 46469944, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa-1.2.0-1.x86_64.rpm" },
        { "name": "composa-1.2.0-linux-arm64.tar.gz", "size": 41219991, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa-1.2.0-linux-arm64.tar.gz" },
        { "name": "composa-1.2.0-linux-x64.tar.gz", "size": 43282934, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa-1.2.0-linux-x64.tar.gz" },
        { "name": "composa-1.2.0-win-arm64-setup.exe", "size": 41475463, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa-1.2.0-win-arm64-setup.exe" },
        { "name": "composa-1.2.0-win-arm64.zip", "size": 55008002, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa-1.2.0-win-arm64.zip" },
        { "name": "composa-1.2.0-win-x64-setup.exe", "size": 45117217, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa-1.2.0-win-x64-setup.exe" },
        { "name": "composa-1.2.0-win-x64.zip", "size": 58169386, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa-1.2.0-win-x64.zip" },
        { "name": "composa_1.2.0_amd64.deb", "size": 33786732, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa_1.2.0_amd64.deb" },
        { "name": "composa_1.2.0_arm64.deb", "size": 30207124, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa_1.2.0_arm64.deb" },
        { "name": "sha256sums.txt", "size": 1140, "browser_download_url": "https://github.com/bianzuilun/Composa/releases/download/v1.2.0/sha256sums.txt" }
      ]
    }
    """;

    private const string LatestEndpoint = "https://api.github.com/repos/bianzuilun/Composa/releases/latest";

    public static async Task<ReleaseInfo> Parse(string json)
    {
        var http = new FakeHttp(new Dictionary<string, byte[]> { [LatestEndpoint] = Encoding.UTF8.GetBytes(json) });
        var release = await new GitHubReleaseSource(http).Latest(TestContext.Current.CancellationToken);
        Assert.NotNull(release);
        return release;
    }

    [Fact]
    public async Task The_release_brings_its_files_and_its_checksums()
    {
        var release = await Parse(V120);
        Assert.Equal("v1.2.0", release.Tag);
        Assert.Equal(13, release.Assets.Count);
        var deb = release.Assets.Single(a => a.Name == "composa_1.2.0_amd64.deb");
        Assert.Equal("https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa_1.2.0_amd64.deb", deb.Url);
        Assert.Equal(33786732, deb.Size);
        Assert.Equal("https://github.com/bianzuilun/Composa/releases/download/v1.2.0/sha256sums.txt", release.Checksums?.Url);
    }

    /// <summary>Nothing identifying goes out with the request: the same bare User-Agent as ever, and no version.</summary>
    [Fact]
    public async Task The_request_says_nothing_about_the_machine()
    {
        var http = new FakeHttp(new Dictionary<string, byte[]> { [LatestEndpoint] = Encoding.UTF8.GetBytes(V120) });
        await new GitHubReleaseSource(http).Latest(TestContext.Current.CancellationToken);
        var request = Assert.Single(http.Requests);
        Assert.Equal("Composa", string.Join(" ", request.Headers.UserAgent.Select(u => u.ToString())));
    }

    /// <summary>Only this repository's releases, over HTTPS, are ever fetched, whatever a response lists.</summary>
    [Fact]
    public async Task Files_from_anywhere_else_are_left_out()
    {
        var json = V120
            .Replace("https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa_1.2.0_amd64.deb", "https://example.com/composa_1.2.0_amd64.deb")
            .Replace("https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa-1.2.0-1.x86_64.rpm", "http://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa-1.2.0-1.x86_64.rpm")
            .Replace("https://github.com/bianzuilun/Composa/releases/download/v1.2.0/composa-1.2.0-linux-x64.tar.gz", "https://github.com/someone/Composa/releases/download/v1.2.0/composa-1.2.0-linux-x64.tar.gz");
        var release = await Parse(json);
        Assert.Equal(10, release.Assets.Count);
        Assert.Null(ReleaseAssets.For(InstallKind.Deb, Architecture.X64, release));
        Assert.Null(ReleaseAssets.For(InstallKind.Rpm, Architecture.X64, release));
        Assert.Null(ReleaseAssets.For(InstallKind.Tarball, Architecture.X64, release));
    }

    [Theory]
    [InlineData(InstallKind.Deb, Architecture.X64, "composa_1.2.0_amd64.deb")]
    [InlineData(InstallKind.Deb, Architecture.Arm64, "composa_1.2.0_arm64.deb")]
    [InlineData(InstallKind.Rpm, Architecture.X64, "composa-1.2.0-1.x86_64.rpm")]
    [InlineData(InstallKind.Rpm, Architecture.Arm64, "composa-1.2.0-1.aarch64.rpm")]
    [InlineData(InstallKind.AppImage, Architecture.X64, "Composa-1.2.0-x86_64.AppImage")]
    [InlineData(InstallKind.AppImage, Architecture.Arm64, "Composa-1.2.0-aarch64.AppImage")]
    [InlineData(InstallKind.Tarball, Architecture.X64, "composa-1.2.0-linux-x64.tar.gz")]
    [InlineData(InstallKind.Tarball, Architecture.Arm64, "composa-1.2.0-linux-arm64.tar.gz")]
    [InlineData(InstallKind.WindowsInstaller, Architecture.X64, "composa-1.2.0-win-x64-setup.exe")]
    [InlineData(InstallKind.WindowsInstaller, Architecture.Arm64, "composa-1.2.0-win-arm64-setup.exe")]
    [InlineData(InstallKind.WindowsZip, Architecture.X64, "composa-1.2.0-win-x64.zip")]
    [InlineData(InstallKind.WindowsZip, Architecture.Arm64, "composa-1.2.0-win-arm64.zip")]
    public async Task Each_install_gets_its_own_file(InstallKind kind, Architecture architecture, string expected)
        => Assert.Equal(expected, ReleaseAssets.For(kind, architecture, await Parse(V120))?.Name);

    /// <summary>A build from a repository leaves updates to its package manager, and a download would go around it.</summary>
    [Fact]
    public async Task A_managed_build_is_offered_no_file()
    {
        var release = await Parse(V120);
        Assert.Null(ReleaseAssets.Offered(UpdateChannel.Managed, InstallKind.Deb, Architecture.X64, release));
        Assert.Equal("composa_1.2.0_amd64.deb", ReleaseAssets.Offered(UpdateChannel.GitHub, InstallKind.Deb, Architecture.X64, release)?.Name);
    }

    [Fact]
    public async Task A_developer_build_is_offered_no_file()
    {
        var release = await Parse(V120);
        Assert.Null(ReleaseAssets.For(InstallKind.Developer, Architecture.X64, release));
    }

    [Fact]
    public async Task A_processor_nothing_is_built_for_is_offered_no_file()
    {
        var release = await Parse(V120);
        Assert.Null(ReleaseAssets.For(InstallKind.Tarball, Architecture.X86, release));
        Assert.Null(ReleaseAssets.For(InstallKind.WindowsZip, Architecture.Arm, release));
    }

    [Fact]
    public async Task A_release_missing_the_file_offers_none()
    {
        var release = await Parse(V120.Replace("composa_1.2.0_amd64.deb", "composa_1.2.0_amd64.deb.sig"));
        Assert.Null(ReleaseAssets.For(InstallKind.Deb, Architecture.X64, release));
        // The other architecture is still there.
        Assert.Equal("composa_1.2.0_arm64.deb", ReleaseAssets.For(InstallKind.Deb, Architecture.Arm64, release)?.Name);
    }

    /// <summary>
    /// dpkg and rpm cannot hold MinVer's '-', so a pre-release is spelled differently in each format
    /// (common.sh); the file is found all the same.
    /// </summary>
    [Fact]
    public void A_prerelease_is_found_under_each_formats_spelling()
    {
        var release = new ReleaseInfo("v1.3.0-beta.1", "https://github.com/bianzuilun/Composa/releases/tag/v1.3.0-beta.1", true,
        [
            Asset("composa_1.3.0.beta.1_amd64.deb"),
            Asset("composa-1.3.0-0.beta.1.x86_64.rpm"),
            Asset("Composa-1.3.0-beta.1-x86_64.AppImage"),
            Asset("composa-1.3.0-beta.1-win-x64-setup.exe"),
            Asset("composa-1.3.0-beta.1-win-x64.zip"),
        ]);
        Assert.Equal("composa_1.3.0.beta.1_amd64.deb", ReleaseAssets.For(InstallKind.Deb, Architecture.X64, release)?.Name);
        Assert.Equal("composa-1.3.0-0.beta.1.x86_64.rpm", ReleaseAssets.For(InstallKind.Rpm, Architecture.X64, release)?.Name);
        Assert.Equal("Composa-1.3.0-beta.1-x86_64.AppImage", ReleaseAssets.For(InstallKind.AppImage, Architecture.X64, release)?.Name);
        // The installer's name ends like the zip's with "-setup" added, and neither is taken for the other.
        Assert.Equal("composa-1.3.0-beta.1-win-x64-setup.exe", ReleaseAssets.For(InstallKind.WindowsInstaller, Architecture.X64, release)?.Name);
        Assert.Equal("composa-1.3.0-beta.1-win-x64.zip", ReleaseAssets.For(InstallKind.WindowsZip, Architecture.X64, release)?.Name);
    }

    private static ReleaseAsset Asset(string name) => new(name, "https://github.com/bianzuilun/Composa/releases/download/v1.3.0-beta.1/" + name, 1);
}
