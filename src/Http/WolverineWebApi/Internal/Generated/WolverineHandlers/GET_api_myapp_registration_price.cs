// <auto-generated/>
#pragma warning disable
using Microsoft.AspNetCore.Routing;
using System;
using System.Linq;
using Wolverine.Http;

namespace Internal.Generated.WolverineHandlers
{
    // START: GET_api_myapp_registration_price
    public class GET_api_myapp_registration_price : Wolverine.Http.HttpHandler
    {
        private readonly Wolverine.Http.WolverineHttpOptions _wolverineHttpOptions;

        public GET_api_myapp_registration_price(Wolverine.Http.WolverineHttpOptions wolverineHttpOptions) : base(wolverineHttpOptions)
        {
            _wolverineHttpOptions = wolverineHttpOptions;
        }



        public override async System.Threading.Tasks.Task Handle(Microsoft.AspNetCore.Http.HttpContext httpContext)
        {
            int numberOfMembers = default;
            int.TryParse(httpContext.Request.Query["numberOfMembers"], out numberOfMembers);
            var decimalValue = WolverineWebApi.Bugs.MyAppLandingEndpoint.GetRegistrationPrice(numberOfMembers);
            await WriteJsonAsync(httpContext, decimalValue);
        }

    }

    // END: GET_api_myapp_registration_price
    
    
}
