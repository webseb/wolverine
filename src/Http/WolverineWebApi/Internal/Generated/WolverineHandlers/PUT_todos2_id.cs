// <auto-generated/>
#pragma warning disable
using Marten;
using Microsoft.AspNetCore.Routing;
using System;
using System.Linq;
using Wolverine.Http;

namespace Internal.Generated.WolverineHandlers
{
    // START: PUT_todos2_id
    public class PUT_todos2_id : Wolverine.Http.HttpHandler
    {
        private readonly Wolverine.Http.WolverineHttpOptions _wolverineHttpOptions;
        private readonly Marten.ISessionFactory _sessionFactory;

        public PUT_todos2_id(Wolverine.Http.WolverineHttpOptions wolverineHttpOptions, Marten.ISessionFactory sessionFactory) : base(wolverineHttpOptions)
        {
            _wolverineHttpOptions = wolverineHttpOptions;
            _sessionFactory = sessionFactory;
        }



        public override async System.Threading.Tasks.Task Handle(Microsoft.AspNetCore.Http.HttpContext httpContext)
        {
            await using var documentSession = _sessionFactory.OpenSession();
            if (!int.TryParse((string)httpContext.GetRouteValue("id"), out var id))
            {
                httpContext.Response.StatusCode = 404;
                return;
            }


            var (request, jsonContinue) = await ReadJsonAsync<WolverineWebApi.Samples.UpdateRequest>(httpContext);
            if (jsonContinue == Wolverine.HandlerContinuation.Stop) return;
            (var todo, var result1) = await WolverineWebApi.Samples.Update2Endpoint.LoadAsync(id, request, documentSession).ConfigureAwait(false);
            if (!(result1 is Wolverine.Http.WolverineContinue))
            {
                await result1.ExecuteAsync(httpContext).ConfigureAwait(false);
                return;
            }

            var todo_response = WolverineWebApi.Samples.Update2Endpoint.Put(id, request, todo, documentSession);
            
            // Commit any outstanding Marten changes
            await documentSession.SaveChangesAsync(httpContext.RequestAborted).ConfigureAwait(false);

            await WriteJsonAsync(httpContext, todo_response);
        }

    }

    // END: PUT_todos2_id
    
    
}

