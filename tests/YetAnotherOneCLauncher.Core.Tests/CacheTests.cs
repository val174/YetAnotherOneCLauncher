using System.Globalization;
using YetAnotherOneCLauncher.Core.Cache;
using static YetAnotherOneCLauncher.Core.Tests.PlatformTestData;

namespace YetAnotherOneCLauncher.Core.Tests;

public class CacheTests
{
    private const string IdA = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string IdB = "bbbbbbbb-1111-2222-3333-444444444444";

    [Fact]
    public void Scanner_takes_only_guid_directories_and_sums_nested_files()
    {
        using var temp = new TempDirectory();
        var local = temp.Combine("local");
        var roaming = temp.Combine("roaming");
        WriteFile(Path.Combine(local, IdA.ToUpperInvariant(), "vrs", "a.bin"), 1000);
        WriteFile(Path.Combine(local, IdA.ToUpperInvariant(), "b.bin"), 24);
        WriteFile(Path.Combine(roaming, IdA, "def.usr"), 10);
        WriteFile(Path.Combine(local, "tmplts", "big.bin"), 5000); // служебный каталог платформы
        WriteFile(Path.Combine(local, "EmptyIB", "x.bin"), 5000);
        WriteFile(Path.Combine(local, "1cv8.pfl"), 5000);
        Directory.CreateDirectory(Path.Combine(local, IdB));

        var scan = CacheScanner.Scan([new CacheRoot(local, CacheLocation.Local), new CacheRoot(roaming, CacheLocation.Roaming), new CacheRoot(temp.Combine("нет"), CacheLocation.Local)]);

        Assert.Empty(scan.Warnings);
        Assert.Equal(3, scan.Directories.Count);
        var a = scan.Directories.Single(d => d.Id == IdA && d.Location == CacheLocation.Local);
        Assert.Equal(1024, a.SizeBytes);
        Assert.Equal(10, scan.Directories.Single(d => d.Location == CacheLocation.Roaming).SizeBytes);
        Assert.Equal(0, scan.Directories.Single(d => d.Id == IdB).SizeBytes);
    }

    [Fact]
    public void Report_matches_bases_by_id_and_finds_orphans()
    {
        var scan = new CacheScanResult(
        [
            new CacheDirectory(IdA, "/l/" + IdA, CacheLocation.Local, 3000, DateTimeOffset.Now),
            new CacheDirectory(IdA, "/r/" + IdA, CacheLocation.Roaming, 100, DateTimeOffset.Now),
            new CacheDirectory(IdB, "/l/" + IdB, CacheLocation.Local, 5000, DateTimeOffset.Now),
        ], []);
        var withBraces = InfoBase("Connect=File=\"C:\\A\";", "ID={" + IdA.ToUpperInvariant() + "}");
        var withoutId = InfoBase("Connect=File=\"C:\\B\";");

        var report = CacheReport.Build(scan, [withBraces, withoutId]);

        Assert.Equal(8100, report.TotalBytes);
        Assert.Equal(5000, report.OrphanBytes);
        Assert.True(report.Owners[0].IsOrphan); // самый большой — первым
        var owner = report.For(withBraces);
        Assert.NotNull(owner);
        Assert.Equal((3000L, 100L), (owner.LocalBytes, owner.RoamingBytes));
        Assert.Single(owner.In(local: true, roaming: false));
        Assert.Equal(2, owner.In(local: true, roaming: true).Count());
        Assert.Null(report.For(withoutId));
    }

    [Fact]
    public void Cleaner_skips_busy_and_foreign_directories()
    {
        using var temp = new TempDirectory();
        var free = CacheDir(temp.Combine(IdA), 10);
        var busy = CacheDir(temp.Combine(IdB), 20);
        var foreign = CacheDir(temp.Combine("tmplts"), 30);
        var missing = new CacheDirectory(IdA, temp.Combine("gone", IdA), CacheLocation.Local, 0, DateTimeOffset.Now);
        var removed = new List<string>();

        var result = CacheCleaner.Clean(
            [free, busy, foreign, missing],
            path =>
            {
                removed.Add(path);
                CacheCleaner.DeletePermanently(path);
            },
            path => path == busy.Path);

        Assert.Equal(new[] { free.Path }, removed);
        Assert.False(Directory.Exists(free.Path));
        Assert.True(Directory.Exists(busy.Path));
        Assert.True(Directory.Exists(foreign.Path));
        Assert.Equal(
            new[] { CacheCleanStatus.Removed, CacheCleanStatus.InUse, CacheCleanStatus.Failed, CacheCleanStatus.Removed },
            result.Items.Select(i => i.Status));
        Assert.Equal(10, result.RemovedBytes);
        Assert.Equal(2, result.Problems.Count());
    }

    [Fact]
    public void Failed_removal_is_reported_not_thrown()
    {
        using var temp = new TempDirectory();
        var directory = CacheDir(temp.Combine(IdA), 10);

        var result = CacheCleaner.Clean([directory], _ => throw new IOException("корзина недоступна"), _ => false);

        var item = Assert.Single(result.Items);
        Assert.Equal(CacheCleanStatus.Failed, item.Status);
        Assert.Equal("корзина недоступна", item.Message);
    }

    [Fact]
    public void Permanent_delete_removes_read_only_files()
    {
        using var temp = new TempDirectory();
        var file = temp.Combine(IdA, "sub", "ro.bin");
        WriteFile(file, 5);
        File.SetAttributes(file, FileAttributes.ReadOnly);

        CacheCleaner.DeletePermanently(temp.Combine(IdA));

        Assert.False(Directory.Exists(temp.Combine(IdA)));
    }

    [Theory]
    [InlineData(0L, "0 Б")]
    [InlineData(1023L, "1023 Б")]
    [InlineData(1536L, "1.5 КБ")]
    [InlineData(10L * 1024 * 1024 + 300 * 1024, "10.3 МБ")]
    [InlineData(1_787_931_583L, "1.67 ГБ")]
    [InlineData(-5L, "0 Б")]
    public void Byte_size_is_human_readable(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSize.Format(bytes, CultureInfo.InvariantCulture));
    }

    private static CacheDirectory CacheDir(string path, int bytes)
    {
        WriteFile(Path.Combine(path, "data.bin"), bytes);
        return new CacheDirectory(Path.GetFileName(path), path, CacheLocation.Local, bytes, DateTimeOffset.Now);
    }

    private static void WriteFile(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }
}
