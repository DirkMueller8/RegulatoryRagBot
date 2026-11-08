using Microsoft.KernelMemory;

namespace RegulatoryRagBot;

public sealed class IngestionService(IKernelMemory memory, Corpus corpus)
{
    public async Task IngestFolderAsync(CancellationToken ct = default)
    {
        var docsPath = Path.GetFullPath(corpus.Path);

        if (!Directory.Exists(docsPath))
        {
            Console.WriteLine($"Documents folder not found: {docsPath}");
            return;
        }

        var files = Directory.GetFiles(docsPath, "*.md", SearchOption.AllDirectories);

        if (files.Length == 0)
        {
            Console.WriteLine($"No .md files found in: {docsPath}");
            return;
        }

        Console.WriteLine($"Found {files.Length} Markdown file(s) in {docsPath}");
        Console.WriteLine();

        int ingested = 0, skipped = 0, failed = 0;

        foreach (var file in files)
        {
            var docId        = BuildDocumentId(file, docsPath);
            var relativePath = Path.GetRelativePath(docsPath, file);

            // IsDocumentReadyAsync returns true when a document with this ID has
            // already been fully chunked, embedded, and stored in Qdrant.
            // Skipping avoids re-paying the embedding API cost on every run.
            if (await memory.IsDocumentReadyAsync(docId, index: corpus.IndexName, cancellationToken: ct))
            {
                WriteColored($"  [SKIP] {relativePath}", ConsoleColor.DarkGray);
                skipped++;
                continue;
            }

            if (await ImportFileAsync(file, docId, relativePath, ct))
                ingested++;
            else
                failed++;
        }

        Console.WriteLine();
        Console.WriteLine($"Done — {ingested} ingested, {skipped} skipped, {failed} failed.");
    }

    // Deletes any existing index entry for the file and re-imports it from disk,
    // picking up content changes that IngestFolderAsync's skip check would otherwise miss
    // (the skip check only looks at whether the document ID is already indexed, not
    // whether the underlying file content changed since).
    public async Task ReingestFileAsync(string relativePath, CancellationToken ct = default)
    {
        var docsPath = Path.GetFullPath(corpus.Path);
        var file     = Path.Combine(docsPath, relativePath);

        if (!File.Exists(file))
        {
            Console.WriteLine($"File not found: {file}");
            return;
        }

        var docId = BuildDocumentId(file, docsPath);

        await memory.DeleteDocumentAsync(docId, index: corpus.IndexName, cancellationToken: ct);
        await ImportFileAsync(file, docId, relativePath, ct);
    }

    private async Task<bool> ImportFileAsync(string file, string docId, string relativePath, CancellationToken ct)
    {
        Console.Write($"  [....]  {relativePath}");

        try
        {
            // AddTag attaches metadata to every chunk derived from this document.
            // The tags surface verbatim in MemoryAnswer.RelevantSources, giving
            // users a precise citation ("Article 5, GDPR.md") rather than a chunk ID.
            await memory.ImportDocumentAsync(
                new Document(docId)
                    .AddFile(file)
                    .AddTag("source",   relativePath)
                    .AddTag("filename", Path.GetFileName(file)),
                index: corpus.IndexName,
                cancellationToken: ct
            );

            Console.Write("\r");
            WriteColored($"  [ OK ]  {relativePath}", ConsoleColor.Green);
            return true;
        }
        catch (Exception ex)
        {
            Console.Write("\r");
            WriteColored($"  [FAIL]  {relativePath}: {ex.Message}", ConsoleColor.Red);
            return false;
        }
    }

    // Produces a stable, URL-safe document ID from the file's relative path so
    // re-runs on the same files always produce the same ID (enabling the skip check).
    private static string BuildDocumentId(string filePath, string basePath)
    {
        var relative = Path.GetRelativePath(basePath, filePath);
        return relative
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_')
            .Replace(' ', '-')
            .ToLowerInvariant();
    }

    private static void WriteColored(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
