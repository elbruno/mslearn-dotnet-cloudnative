using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel;
using Products.Data;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.EntityFrameworkCore;
using AIEntities;
using Microsoft.SemanticKernel.ChatCompletion;


#pragma warning disable SKEXP0003, SKEXP0001, SKEXP0010, SKEXP0011, SKEXP0050, SKEXP0052

namespace Products.Data;

public static class MemoryContext
{
    private static ISemanticTextMemory? memory;
    private static MemoryBuilder? memoryBuilder;
    private static Kernel? kernel;
    private static KernelPlugin? kernelPlugIn;
    const string MemoryCollectionName = "products";

    internal static void InitSKKernel()
    {
        if (kernel != null)
            return;

        var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
        var builder = Kernel.CreateBuilder();
        builder.AddAzureOpenAIChatCompletion(
            config["AZURE_OPENAI_MODEL-GPT3.5"],
            config["AZURE_OPENAI_ENDPOINT"],
            config["AZURE_OPENAI_APIKEY"]
            );
        kernel = builder.Build();

        // Load Plugins collection
        var pluginsDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), @"", "plugins");

        // Show the plugins directory path in the console
        Console.WriteLine($"Plugins directory path: {pluginsDirectoryPath}");

        kernelPlugIn = kernel.ImportPluginFromPromptDirectory(pluginsDirectoryPath);
    }


    internal static void InitMemoryBuilder()
    {
        if (memoryBuilder != null)
            return;

        var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
        memoryBuilder = new MemoryBuilder();
        _ = memoryBuilder.WithAzureOpenAITextEmbeddingGeneration(
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
            var productInfo = $"[{product.Name}] is a product that costs [{product.Price}] and is described as [{product.Description}]";

            await memory.SaveInformationAsync(
                collection: MemoryCollectionName,
                text: product.Description,
                id: product.Id.ToString(),
                description: productInfo);
        }
    }

    public static async Task<AISearchResponse> Search(string search, ProductDataContext db)
    {
        InitSKKernel();

        // search the vector database for the most similar product        
        var memorySearchResult = await memory.SearchAsync(MemoryCollectionName, search).FirstOrDefaultAsync();
        var prodId = memorySearchResult.Metadata.Id;
        var firstProduct = await db.Product.FirstOrDefaultAsync(p => p.Id == int.Parse(prodId));

        // create a response object
        AISearchResponse response = new AISearchResponse
        {
            Products = [firstProduct]
        };

        // build a human friendly message to show the response
        var q1 = new KernelArguments()
        {
            ["productid"] = firstProduct.Id.ToString(),
            ["productname"] = firstProduct.Name,
            ["productdescription"] = firstProduct.Description,
            ["productprice"] = firstProduct.Price.ToString(),
            ["question"] = search
        };
        var result = await kernel.InvokeAsync(kernelPlugIn["SearchResponse"], q1);
        response.Response = result.ToString();

        return response;

    }
}
