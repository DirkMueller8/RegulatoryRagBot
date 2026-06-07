namespace RegulatoryRagBot;

public sealed class AppConfig
{
    public OpenAiSettings OpenAI { get; set; } = new();
    public QdrantSettings Qdrant { get; set; } = new();
    public IngestionSettings Ingestion { get; set; } = new();
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
    public string DocumentsPath { get; set; } = @".\md_documents";

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
