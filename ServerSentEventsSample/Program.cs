using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var tasks = new Dictionary<string, Task>();

app.MapPost("/operations", (HttpContext httpContext, LinkGenerator linkGenerator) =>
{
    var id = Guid.NewGuid().ToString();
    tasks.Add(id, Task.Delay(5000 + Random.Shared.Next(0, 5000)));
    return new
    {
        id,
        links =
            new[]
            {
                new Link("status_check", linkGenerator.GetUriByName(httpContext, Names.StatusCheck, new { id })!,
                    "EventSource")
            }
    };
}).WithName(Names.CreateOperation);

app.MapGet("/operations/{id}/status", async (string id, HttpContext httpContext, LinkGenerator linkGenerator) =>
{
    if (tasks.TryGetValue(id, out var task))
    {
        httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
        while (!task.IsCompleted)
        {
            await httpContext.Response.WriteAsync("event: pending\n");
            await httpContext.Response.WriteAsync($"data: {{ \"timestamp\": \"{DateTimeOffset.UtcNow:O}\" }}\n\n");
            await httpContext.Response.Body.FlushAsync();
            Thread.Sleep(500);
        }

        await httpContext.Response.WriteAsync("event: completed\n");
        await httpContext.Response.WriteAsync(
            $"data: {{ \"timestamp\": \"{DateTimeOffset.UtcNow:O}\", \"resource_uri\":\"{linkGenerator.GetUriByName(httpContext, Names.GetOperation, new { id })}\" }}\n\n");
        await httpContext.Response.Body.FlushAsync();
    }
}).WithName(Names.StatusCheck);

app.MapGet("/operations/{id}", (string id) =>
{
    if (tasks.TryGetValue(id, out var _))
    {
        return Results.Ok(new { msg = "Resource was created" });
    }

    return Results.NotFound();
}).WithName(Names.GetOperation);

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseHttpsRedirection();
app.Run();

sealed record Link(string Rel, string Href, string Action);

static class Names
{
    public const string GetOperation = "get_operation";
    public const string CreateOperation = "create_operation";
    public const string StatusCheck = "status_check";
}