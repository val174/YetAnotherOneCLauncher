using System.Text;
using YetAnotherOneCLauncher.Core.IO;
using YetAnotherOneCLauncher.Core.Text;

namespace YetAnotherOneCLauncher.Core.Parsing;

internal static class V8iWriter
{
    public static string Serialize(V8iDocument document)
    {
        var newLine = document.Format.NewLine;
        var builder = new StringBuilder();
        var first = true;

        void Append(string line)
        {
            if (!first)
            {
                builder.Append(newLine);
            }

            builder.Append(line);
            first = false;
        }

        foreach (var line in document.Preamble)
        {
            Append(line.ToString());
        }

        foreach (var section in document.Sections)
        {
            Append(section.HeaderText);
            foreach (var line in section.Lines)
            {
                Append(line.ToString());
            }
        }

        if (document.EndsWithNewLine && !first)
        {
            builder.Append(newLine);
        }

        return builder.ToString();
    }

    public static Task SaveAsync(
        V8iDocument document,
        string path,
        string? backupPath,
        CancellationToken cancellationToken)
    {
        var bytes = TextFileCodec.Encode(Serialize(document), document.Format);
        return AtomicFileWriter.WriteAllBytesAsync(path, bytes, backupPath, cancellationToken);
    }
}
