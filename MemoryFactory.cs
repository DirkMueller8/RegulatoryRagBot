using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Configuration;

namespace RegulatoryRagBot;

public static class MemoryFactory
{
    public static IKernelMemory Build(AppConfig config)
    {
        var openAiConfig = new OpenAIConfig
        {
            APIKey         = config.OpenAI.ApiKey,
            TextModel      = config.OpenAI.ChatModel,        // used by AskAsync for generation
            EmbeddingModel = config.OpenAI.EmbeddingModel    // used for vectorising chunks + queries
        };

        // TextPartitioningOptions.MaxTokensPerParagraph controls chunk size.
        // OverlappingTokens controls how many tokens the adjacent chunks share.
        var partitioning = new TextPartitioningOptions
        {
            MaxTokensPerParagraph = config.Ingestion.MaxTokensPerChunk,
            OverlappingTokens     = config.Ingestion.OverlapTokens
        };

        var storagePath = Path.Combine(AppContext.BaseDirectory, "km-storage");

        return new KernelMemoryBuilder()
            .WithOpenAI(openAiConfig)
            .WithQdrantMemoryDb(config.Qdrant.Endpoint)
            .WithSimpleFileStorage(storagePath)
            .WithCustomTextPartitioningOptions(partitioning)
            .Build<MemoryServerless>();
    }
}
