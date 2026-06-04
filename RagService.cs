using Microsoft.KernelMemory;

namespace RegulatoryRagBot;

public sealed class RagService(IKernelMemory memory, AppConfig config)
{
    public async Task AskAsync(string question, CancellationToken ct = default)
    {
        Console.WriteLine("Searching knowledge base...");
        Console.WriteLine();

        MemoryAnswer answer = await memory.AskAsync(
            question,
            index: config.Qdrant.IndexName,
            minRelevance: config.Ingestion.MinRelevance,
            cancellationToken: ct
        );

        if (answer.NoResult)
        {
            WriteColored(
                "No relevant content found for this question. " +
                "Ensure the relevant documents have been ingested.",
                ConsoleColor.Yellow);
            return;
        }

        // ── Answer ───────────────────────────────────────────────────────────
        WriteColored("Answer:", ConsoleColor.Green);
        Console.WriteLine(answer.Result);
        Console.WriteLine();

        // ── Citations ─────────────────────────────────────────────────────────
        // For regulatory work, showing which document and passage produced the
        // answer is as important as the answer itself.
        if (answer.RelevantSources.Count > 0)
        {
            WriteColored($"Sources ({answer.RelevantSources.Count}):", ConsoleColor.Cyan);

            foreach (var citation in answer.RelevantSources)
            {
                // Prefer the "source" tag we attached during ingestion (relative path),
                // fall back to the raw source name if not present.
                var sourceLabel = citation.Partitions
                    .SelectMany(p => p.Tags)
                    .Where(t => t.Key == "source")
                    .Select(t => t.Value?.FirstOrDefault())
                    .FirstOrDefault(v => v is not null)
                    ?? citation.SourceName;

                Console.WriteLine();
                WriteColored($"  {sourceLabel}", ConsoleColor.White);

                // Show the top 3 passages by relevance score so the user can
                // verify the answer against the primary text.
                foreach (var partition in citation.Partitions
                    .OrderByDescending(p => p.Relevance)
                    .Take(3))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    Relevance : {partition.Relevance:P0}");

                    var preview = partition.Text.Length > 300
                        ? partition.Text[..300].TrimEnd() + " …"
                        : partition.Text;
                    Console.WriteLine($"    Preview   : {preview}");
                    Console.ResetColor();
                }
            }
        }
    }

    private static void WriteColored(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
