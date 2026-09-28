namespace Press.Service.Http;

using Press.Service.Application;
using Press.Service.Infrastructure;

public static class RecipeHttpEndpoints
{
    public static void MapRecipeHttp(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/recipes");

        api.MapGet("/", async (IPressStore store, CancellationToken ct) =>
            Results.Json(await store.ListRecipesAsync(ct)));

        api.MapGet("/{id}", async (string id, IPressStore store, CancellationToken ct) =>
        {
            var recipe = await store.GetRecipeAsync(id, ct);
            return recipe is null ? Results.NotFound() : Results.Json(recipe);
        });

        api.MapPut("/{id}", async (string id, RecipeDocument body, IPressStore store, CancellationToken ct) =>
        {
            body.RecipeVersionId = id;
            if (string.IsNullOrWhiteSpace(body.ProductName))
                return Results.BadRequest(new { error = "productName required" });
            await store.SaveRecipeAsync(body, ct);
            var saved = await store.GetRecipeAsync(id, ct);
            return Results.Json(saved);
        });

        api.MapPost("/", async (RecipeDocument body, IPressStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.RecipeVersionId))
                body.RecipeVersionId = $"rv-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            if (string.IsNullOrWhiteSpace(body.ProductId))
                body.ProductId = body.ProductName.ToLowerInvariant().Replace(' ', '-');
            await store.SaveRecipeAsync(body, ct);
            var saved = await store.GetRecipeAsync(body.RecipeVersionId, ct);
            return Results.Json(saved);
        });

        api.MapDelete("/{id}", async (string id, IPressStore store, CancellationToken ct) =>
        {
            await store.DeleteRecipeAsync(id, ct);
            return Results.NoContent();
        });

        api.MapPost("/{id}/apply", async (string id, IPressStore store, MachineRuntime runtime, CancellationToken ct) =>
        {
            var recipe = await store.GetRecipeAsync(id, ct);
            if (recipe is null) return Results.NotFound();
            await runtime.LoadJobAsync(recipe.ToJob(), ct);
            return Results.Json(new { ok = true, recipeVersionId = id, state = (await runtime.GetSnapshotAsync(ct)).State.ToString() });
        });
    }
}
