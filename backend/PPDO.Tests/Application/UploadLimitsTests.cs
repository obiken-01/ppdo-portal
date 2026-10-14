using System.IO.Compression;
using System.Text;
using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// Pins PPDO-199's upload limits: the size cap (declared and actual) and the workbook expansion
/// check. Every refusal is asserted by its <see cref="UploadRefusal"/>, so a test cannot pass
/// because the buffer happened to come back empty for another reason.
/// </summary>
public sealed class UploadLimitsTests
{
    // ── Size cap ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task BufferAsync_SmallUpload_IsBufferedAtPositionZero()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("code,name\r\nGF,General Fund\r\n");

        UploadBuffer result = await UploadLimits.BufferAsync(new MemoryStream(bytes), bytes.Length);

        Assert.False(result.IsRefused);
        Assert.Equal(0, result.Body!.Position);
        Assert.Equal(bytes, result.Body.ToArray());
    }

    [Fact]
    public async Task BufferAsync_DeclaredLengthOverTheCap_IsRefusedWithoutReading()
    {
        UploadBuffer result = await UploadLimits.BufferAsync(
            new UnreadableStream(), declaredLength: UploadLimits.MaxUploadBytes + 1);

        Assert.Equal(UploadRefusal.TooLarge, result.Refusal);
        Assert.Null(result.Body);
    }

    [Fact]
    public async Task BufferAsync_BodyOverTheCap_WithNoDeclaredLength_IsRefused()
    {
        // The case Content-Length cannot catch: a chunked or lying request.
        UploadBuffer result = await UploadLimits.BufferAsync(
            new ZeroStream(UploadLimits.MaxUploadBytes + 1), declaredLength: null);

        Assert.Equal(UploadRefusal.TooLarge, result.Refusal);
    }

    [Fact]
    public async Task BufferAsync_BodyOverTheCap_UnderAnUnderstatedLength_IsRefused()
    {
        UploadBuffer result = await UploadLimits.BufferAsync(
            new ZeroStream(UploadLimits.MaxUploadBytes + 1), declaredLength: 1024);

        Assert.Equal(UploadRefusal.TooLarge, result.Refusal);
    }

    [Fact]
    public async Task BufferAsync_BodyExactlyAtTheCap_IsAccepted()
    {
        UploadBuffer result = await UploadLimits.BufferAsync(
            new ZeroStream(UploadLimits.MaxUploadBytes), UploadLimits.MaxUploadBytes);

        Assert.False(result.IsRefused);
        Assert.Equal(UploadLimits.MaxUploadBytes, result.Body!.Length);
    }

    // ── Workbook expansion ───────────────────────────────────────────────────

    [Fact]
    public async Task BufferAsync_OrdinaryWorkbook_IsAccepted()
    {
        byte[] zip = Zip(("xl/workbook.xml", 4_000), ("xl/worksheets/sheet1.xml", 50_000));

        UploadBuffer result = await UploadLimits.BufferAsync(new MemoryStream(zip), zip.Length);

        Assert.False(result.IsRefused);
        Assert.Equal(zip, result.Body!.ToArray());
    }

    [Fact]
    public async Task BufferAsync_WorkbookThatExpandsPastTheLimit_IsRefused()
    {
        // A zip bomb in miniature: zeros compress ~1000:1, so this is well under the upload cap
        // while declaring more than the expansion limit.
        byte[] zip = Zip(("xl/worksheets/sheet1.xml", UploadLimits.MaxExpandedWorkbookBytes + 1));
        Assert.True(zip.Length < UploadLimits.MaxUploadBytes);

        UploadBuffer result = await UploadLimits.BufferAsync(new MemoryStream(zip), zip.Length);

        Assert.Equal(UploadRefusal.WorkbookTooLarge, result.Refusal);
    }

    [Fact]
    public async Task BufferAsync_WorkbookWithTooManyParts_IsRefused()
    {
        byte[] zip = Zip(Enumerable.Range(0, UploadLimits.MaxWorkbookEntries + 1)
            .Select(i => ($"xl/p{i}.xml", 0L)).ToArray());

        UploadBuffer result = await UploadLimits.BufferAsync(new MemoryStream(zip), zip.Length);

        Assert.Equal(UploadRefusal.WorkbookTooLarge, result.Refusal);
    }

    [Fact]
    public async Task BufferAsync_DamagedZip_IsPassedThroughForTheParserToReport()
    {
        // Starts like a zip, is not one. The import's own parser gives this its clearer error.
        byte[] bytes = [0x50, 0x4B, 0x03, 0x04, 0x01, 0x02, 0x03];

        UploadBuffer result = await UploadLimits.BufferAsync(new MemoryStream(bytes), bytes.Length);

        Assert.False(result.IsRefused);
        Assert.Equal(bytes, result.Body!.ToArray());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static byte[] Zip(params (string Name, long ZeroBytes)[] entries)
    {
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            byte[] zeros = new byte[1024 * 1024];
            foreach ((string name, long size) in entries)
            {
                using Stream entry = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
                for (long left = size; left > 0; left -= zeros.Length)
                    entry.Write(zeros, 0, (int)Math.Min(zeros.Length, left));
            }
        }
        return output.ToArray();
    }

    /// <summary>A forward-only stream of zeros of a given length, like a request body.</summary>
    private sealed class ZeroStream(long length) : Stream
    {
        private long _left = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count, _left);
            Array.Clear(buffer, offset, n);
            _left -= n;
            return n;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Fails on any read — proves a refusal happened before the body was touched.</summary>
    private sealed class UnreadableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("The body was read although its declared length was over the cap.");
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
