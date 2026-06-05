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

var ingestion = new IngestionService(memory, config);
var rag       = new RagService(memory, config);

// ── Main menu loop ────────────────────────────────────────────────────────────
Console.Clear();
Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine("══════════════════════════════════════════════════");
Console.WriteLine("   Regulatory Document RAG Chatbot");
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
    Console.WriteLine("  [Q] Ask a question");
    Console.WriteLine("  [X] Exit");
    Console.Write("> ");

    var command = (Console.ReadLine() ?? string.Empty).Trim().ToUpperInvariant();
    Console.WriteLine();

    switch (command)
    {
        case "I":
            await ingestion.IngestFolderAsync();
            break;

        case "Q":
            await RunQaSessionAsync(rag);
            break;

        case "X":
            Console.WriteLine("Goodbye.");
            return 0;

        default:
            Console.WriteLine("Unknown command — type I, Q, or X.");
            break;
    }

    Console.WriteLine();
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
