using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel;
using Products.Data;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.EntityFrameworkCore;


#pragma warning disable SKEXP0003, SKEXP0001, SKEXP0010, SKEXP0011, SKEXP0050, SKEXP0052

namespace Products.Data;

public static class MemoryContext
{
    private static ISemanticTextMemory memory;
    private static MemoryBuilder memoryBuilder;
    const string MemoryCollectionName = "products";

    internal static void InitMemoryBuilder()
    {
        // validate if memoryBuilder is already initialized
        if (memoryBuilder != null)
            return;

        var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
        memoryBuilder = new MemoryBuilder();
        memoryBuilder.WithAzureOpenAITextEmbeddingGeneration(
                config["text-embedding-ada-002"],
                config["AZURE_OPENAI_ENDPOINT"],
                config["AZURE_OPENAI_APIKEY"]);
        memoryBuilder.WithMemoryStore(new VolatileMemoryStore());
    }

    internal static async void InitMemory(ProductDataContext db)
    {
        InitMemoryBuilder();
        memory = memoryBuilder.Build();
        await FillProductsAsync(db);
    }

    public static async Task FillProductsAsync(ProductDataContext db)
    {

        // get a copy of the list of products
        var products = await db.Product.ToListAsync();

        // iterate over the products and add them to the memory
        foreach (var product in products)
        {
            var productInfo = $"{product.Name} is a product that costs {product.Price} and is described as {product.Description}";

            await memory.SaveInformationAsync(
                collection: MemoryCollectionName,
                text: product.Description,
                id: product.Id.ToString(),
                description: productInfo);
        }
    }

    public static async Task<string> Search(string search)
    {
        var response = await memory.SearchAsync(MemoryCollectionName, search).FirstOrDefaultAsync();

        // build a complete string with all the information from the response
        var result = @$"{response.Metadata.Description} \n" +
            @$"Product Id: {response.Metadata.Id} \n" +
            @$"Relevance: {response.Relevance}";
        return result;       
        
    }
}
