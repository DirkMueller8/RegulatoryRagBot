using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Context;

namespace RegulatoryRagBot;

public sealed class RagService(IKernelMemory memory, AppConfig config, Corpus corpus)
{
    // Learning mode: the model may combine the retrieved passages with its own
    // training knowledge. Strict mode: answers come only from the documents.
    public bool LearningMode { get; set; } = config.Rag.LearningMode;

    // Replaces Kernel Memory's "answer only from the facts" prompt in learning mode.
    // {{$facts}}, {{$input}} and {{$notFound}} are filled in by Kernel Memory.
    private const string LearningPrompt = """
        Facts from the user's documents (may be partly or entirely irrelevant):
        {{$facts}}
        ======
        Answer the question below.
        - Use the facts above where they are relevant, and say so ("According to the documents ...").
        - Where the facts are missing or insufficient, answer from your own general knowledge
          and say so explicitly ("From general knowledge, not from the documents: ...").
        - Never present general knowledge as if it came from the documents, and ignore irrelevant facts.
        - If you are unsure or your knowledge may be outdated, say so.
        Question: {{$input}}
        Answer:
        """;

    public async Task AskAsync(string question, CancellationToken ct = default)
    {
        Console.WriteLine(LearningMode
            ? "Searching knowledge base (learning mode: general knowledge allowed)..."
            : "Searching knowledge base (strict mode: documents only)...");
        Console.WriteLine();

        // In learning mode no relevance floor is applied to retrieval, so the model is
        // always called — even when no passage is a good match (e.g. a topic that is
        // not in the documents). The strict floor is applied to the displayed sources below.
        MemoryAnswer answer = await memory.AskAsync(
            question,
            index: corpus.IndexName,
            minRelevance: LearningMode ? 0 : config.Ingestion.MinRelevance,
            context: LearningMode
                ? new RequestContext().SetArg(Constants.CustomContext.Rag.Prompt, LearningPrompt)
                : null,
            cancellationToken: ct
        );

        if (answer.NoResult)
        {
            WriteColored(
                $"No relevant content found in corpus '{corpus.Name}'. " +
                "Ensure its documents have been ingested.",
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
        // Only passages that clear the normal threshold count as sources; weaker
        // matches retrieved in learning mode are not cited.
        var sources = answer.RelevantSources
            .Where(c => c.Partitions.Any(p => p.Relevance >= config.Ingestion.MinRelevance))
            .ToList();

        if (LearningMode && sources.Count == 0)
            WriteColored("No passage in the documents matched well — this answer relies on general knowledge.", ConsoleColor.Yellow);

        if (sources.Count > 0)
        {
            WriteColored($"Sources ({sources.Count}):", ConsoleColor.Cyan);

            foreach (var citation in sources)
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
                    .Where(p => p.Relevance >= config.Ingestion.MinRelevance)
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
