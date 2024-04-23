using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel;
using Products.Data;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.EntityFrameworkCore;
using AIEntities;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Reflection;


#pragma warning disable SKEXP0003, SKEXP0001, SKEXP0010, SKEXP0011, SKEXP0050, SKEXP0052

namespace Products.Data;

public static class MemoryContext
{
    private static ISemanticTextMemory? memory;
    private static MemoryBuilder? memoryBuilder;
    private static Kernel? kernel;
    private static KernelPlugin? kernelPlugIn;
    private static bool kernelPlugInLoadedFromDir = false;
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
        var pluginsDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "Plugins");
        Console.WriteLine($"Plugins directory path: {pluginsDirectoryPath}");
        try
        {
            kernelPlugIn = kernel.ImportPluginFromPromptDirectory(pluginsDirectoryPath);
            kernelPlugInLoadedFromDir = true;
        }
        catch (Exception ex)
        {
            // can't load plugins, continue without them
            kernelPlugInLoadedFromDir = false;
            Console.WriteLine($"Error loading plugins: {ex.Message}");
        }

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

        if (kernelPlugInLoadedFromDir == true)
        {
            var result = await kernel.InvokeAsync(kernelPlugIn["SearchResponse"], q1);
            response.Response = result.ToString();
        }
        else
        {
            var result = await kernel.InvokePromptAsync(GetSearchResponsePrompt(), q1);
            response.Response = result.ToString();
        }

        return response;

    }

    private static string GetSearchResponsePrompt()
    {
        return @"
You are an intelligent assistant helping Contoso Inc clients with their search about outdoor producst.
Use 'you' to refer to the individual asking the questions even if they ask with 'I'.
Answer the following question using only the data provided related to a product in the response below. Do not include the product id.
Do not return markdown format. Do not return HTML format.
Use emojis if applicable.
If you cannot answer using the information below, say you don't know. 

You write your response in a friendly and funny style.

Incorporate the question if provided: {{$question}}
+++++
product id: {{$productid}}
product name: {{$productname}}
product description: {{$productdescription}}
product price: {{$productprice}}
+++++
";
    }
}
