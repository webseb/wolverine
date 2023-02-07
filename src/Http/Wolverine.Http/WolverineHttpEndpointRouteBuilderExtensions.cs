using System.Text.Json;
using JasperFx.CodeGeneration;
using Lamar;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;

namespace Wolverine.Http;

public class WolverineRequiredException : Exception
{
    public WolverineRequiredException(Exception? innerException) : base("Wolverine is either not added to this application through IHostBuilder.UseWolverine() or is invalid", innerException)
    {
    }
}

public static class WolverineHttpEndpointRouteBuilderExtensions
{
    public static void MapWolverineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        WolverineRuntime runtime;

        // TODO -- unit test this behavior
        try
        {
            runtime = (WolverineRuntime)endpoints.ServiceProvider.GetRequiredService<IWolverineRuntime>();
        }
        catch (Exception e)
        {
            throw new WolverineRequiredException(e);
        }

        // TODO -- let folks customize this somehow? Custom policies? Middleware?
        var container = (IContainer)endpoints.ServiceProvider;
        
        // Making sure this exists
        var options = container.GetInstance<WolverineHttpOptions>();
        options.JsonSerializerOptions = container.TryGetInstance<JsonOptions>()?.SerializerOptions ?? new JsonSerializerOptions();
        options.Endpoints = new EndpointGraph(runtime.Options, container);
        options.Endpoints.DiscoverEndpoints();

        endpoints.DataSources.Add(options.Endpoints);
    }
}