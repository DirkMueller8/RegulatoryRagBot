using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using RegulatoryRagBot;

// ── Load configuration ────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()   // OPENAI__APIKEY etc. override appsettings values
    .Build()
    .Get<AppConfig>()!;

// ── Build the shared Kernel Memory instance ───────────────────────────────────
// Both IngestionService and RagService reuse this single instance so that the
// Qdrant connection and OpenAI HTTP client are not duplicated.
IKernelMemory? memory;
try
{
    memory = MemoryFactory.Build(config);
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"ERROR: Could not initialise Kernel Memory.");
    Console.Error.WriteLine($"       Verify Qdrant is running at {config.Qdrant.Endpoint}");
    Console.Error.WriteLine($"       Details: {ex.Message}");
    Console.ResetColor();
    return 1;
}

// ── Select corpus (one subfolder of DocumentsPath = one Qdrant index) ────────
var corpora = Corpus.Discover(config);
if (corpora.Count == 0)
{
    Console.Error.WriteLine($"No corpus subfolders found in: {Path.GetFullPath(config.Ingestion.DocumentsPath)}");
    return 1;
}

var corpus = SelectCorpus(corpora);
if (corpus is null) return 0;

var ingestion = new IngestionService(memory, corpus);
var rag       = new RagService(memory, config, corpus);

// ── Main menu loop ────────────────────────────────────────────────────────────
if (!Console.IsOutputRedirected) Console.Clear();
Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine("══════════════════════════════════════════════════");
Console.WriteLine("   Regulatory Document RAG Chatbot");
Console.WriteLine($"   Corpus  : {corpus.Name} (index: {corpus.IndexName})");
Console.WriteLine($"   Model   : {config.OpenAI.ChatModel}");
Console.WriteLine($"   Embed   : {config.OpenAI.EmbeddingModel}");
Console.WriteLine($"   Chunks  : {config.Ingestion.MaxTokensPerChunk} tokens / {config.Ingestion.OverlapTokens} overlap");
Console.WriteLine($"   Min sim : {config.Ingestion.MinRelevance:P0}");
Console.WriteLine("══════════════════════════════════════════════════");
Console.ResetColor();
Console.WriteLine();

while (true)
{
    Console.WriteLine("  [I] Ingest documents");
    Console.WriteLine("  [R] Re-ingest a document (after editing it)");
    Console.WriteLine("  [Q] Ask a question");
    Console.WriteLine($"  [C] Change corpus (current: {corpus.Name})");
    Console.WriteLine($"  [L] Toggle learning mode (now: {(rag.LearningMode ? "ON — general knowledge allowed" : "OFF — documents only")})");
    Console.WriteLine("  [X] Exit");
    Console.Write("> ");

    var command = (Console.ReadLine() ?? string.Empty).Trim().ToUpperInvariant();
    Console.WriteLine();

    switch (command)
    {
        case "I":
            await ingestion.IngestFolderAsync();
            break;

        case "R":
            Console.Write("Relative path of file to re-ingest (e.g. ISO-27001.md): ");
            var relativePath = (Console.ReadLine() ?? string.Empty).Trim();
            await ingestion.ReingestFileAsync(relativePath);
            break;

        case "Q":
            await RunQaSessionAsync(rag);
            break;

        case "C":
            var selected = SelectCorpus(corpora);
            if (selected is not null)
            {
                var learning = rag.LearningMode;
                corpus    = selected;
                ingestion = new IngestionService(memory, corpus);
                rag       = new RagService(memory, config, corpus) { LearningMode = learning };
                Console.WriteLine($"Active corpus: {corpus.Name} (index: {corpus.IndexName})");
            }
            break;

        case "L":
            rag.LearningMode = !rag.LearningMode;
            Console.WriteLine(rag.LearningMode
                ? "Learning mode ON: answers may add general model knowledge (labelled as such)."
                : "Learning mode OFF: answers use the documents only.");
            break;

        case "X":
            Console.WriteLine("Goodbye.");
            return 0;

        default:
            Console.WriteLine("Unknown command — type I, R, Q, C, L, or X.");
            break;
    }

    Console.WriteLine();
}

// ── Corpus picker: accepts a number or a name; empty input / 'x' cancels ──────
static Corpus? SelectCorpus(List<Corpus> corpora)
{
    while (true)
    {
        Console.WriteLine("Available corpora:");
        for (int i = 0; i < corpora.Count; i++)
            Console.WriteLine($"  [{i + 1}] {corpora[i].Name}");
        Console.Write("Select corpus (number or name, blank to cancel): ");

        var input = (Console.ReadLine() ?? string.Empty).Trim();
        if (input.Length == 0 || input.Equals("x", StringComparison.OrdinalIgnoreCase)) return null;

        if (int.TryParse(input, out var n) && n >= 1 && n <= corpora.Count) return corpora[n - 1];

        var byName = corpora.FirstOrDefault(c => c.Name.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return byName;

        Console.WriteLine("Unknown corpus.");
        Console.WriteLine();
    }
}

// ── Q&A session loop ──────────────────────────────────────────────────────────
static async Task RunQaSessionAsync(RagService rag)
{
    Console.WriteLine("Q&A session started. Type 'back' to return to the menu.");
    Console.WriteLine();

    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("Question: ");
        Console.ResetColor();

        var question = (Console.ReadLine() ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(question)) continue;
        if (question.Equals("back", StringComparison.OrdinalIgnoreCase)) break;

        Console.WriteLine();

        try
        {
            await rag.AskAsync(question);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.WriteLine(new string('─', 50));
        Console.WriteLine();
    }
}
