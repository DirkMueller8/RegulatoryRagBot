namespace RegulatoryRagBot;

public sealed class AppConfig
{
    public OpenAiSettings OpenAI { get; set; } = new();
    public QdrantSettings Qdrant { get; set; } = new();
    public IngestionSettings Ingestion { get; set; } = new();
    public RagSettings Rag { get; set; } = new();
}

public sealed class RagSettings
{
    // Start value of "learning mode": answers may add the model's general knowledge
    // (clearly labelled) to what the documents say. Toggle at runtime with [L].
    public bool LearningMode { get; set; } = true;
}

// One document corpus = one subfolder = one Qdrant index.
public sealed record Corpus(string Name, string Path, string IndexName)
{
    public static List<Corpus> Discover(AppConfig config)
    {
        var root = System.IO.Path.GetFullPath(config.Ingestion.DocumentsPath);
        if (!Directory.Exists(root)) return [];

        return Directory.GetDirectories(root)
            .Select(dir => System.IO.Path.GetFileName(dir))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(name => new Corpus(
                name,
                System.IO.Path.Combine(root, name),
                config.Ingestion.IndexOverrides.TryGetValue(name, out var idx) ? idx : name.ToLowerInvariant()))
            .ToList();
    }
}

public sealed class OpenAiSettings
{
    public string ApiKey { get; set; } = string.Empty;

    // text-embedding-3-large: highest accuracy (3072 dimensions), ~2× cost of -small.
    // For regulatory documents where precision matters, this is the right choice.
    public string EmbeddingModel { get; set; } = "text-embedding-3-large";

    // gpt-4o: best reasoning for complex regulatory language.
    // Swap to gpt-4o-mini to reduce cost once you've validated quality.
    public string ChatModel { get; set; } = "gpt-4o";
}

public sealed class QdrantSettings
{
    public string Endpoint { get; set; } = "http://localhost:6333";

    // One named collection per document corpus.
    // If you later add a second corpus (e.g. internal policies vs. external regulations)
    // simply change this or add a second index name.
    public string IndexName { get; set; } = "regulatory-docs";
}

public sealed class IngestionSettings
{
    // Root folder; each subfolder is one corpus (e.g. documents\iso-27001).
    public string DocumentsPath { get; set; } = @".\documents";

    // Optional corpus-name → Qdrant index-name overrides. A corpus without an
    // entry uses its folder name as the index name. Used to keep a corpus that
    // was embedded earlier under a different index (e.g. "iso-27001" → "regulatory-docs").
    public Dictionary<string, string> IndexOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ── Chunking parameters ────────────────────────────────────────────────────
    // Regulatory text is dense: definitions span sentences, cross-references
    // span paragraphs. 1024-token chunks preserve enough clause context so that
    // an embedding captures the full meaning of a provision, not just a fragment.
    // (General-purpose RAG typically uses 256–512 tokens; legal/regulatory
    // literature recommends 800–1200.)
    public int MaxTokensPerChunk { get; set; } = 1024;

    // 25% overlap (256/1024) ensures that information sitting near a chunk
    // boundary is fully represented in at least one chunk on each side.
    // Without overlap, a cross-reference split across two chunks can be missed.
    public int OverlapTokens { get; set; } = 256;

    // Minimum cosine-similarity score [0–1] a retrieved passage must reach
    // before it is included in the answer. 0.55 is a good balance: strict enough
    // to exclude unrelated passages, loose enough not to miss paraphrased clauses.
    public double MinRelevance { get; set; } = 0.55;
}
