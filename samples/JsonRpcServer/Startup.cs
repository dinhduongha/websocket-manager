using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Bamboo.WebSocketManager;
using Microsoft.AspNetCore.HttpOverrides;
using IoT;
namespace IoTApplication
{
    public class Startup
    {
        public void Configure(IApplicationBuilder app, IServiceProvider serviceProvider)
        {
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
            });
            app.UseWebSockets();
            app.MapWebSocketManager("/iot/device", serviceProvider.GetService<IoTDeviceHandler>());
            app.MapWebSocketManager("/iot/admin", serviceProvider.GetService<IoTAdminHandler>());

            app.UseStaticFiles();
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddWebSocketManager<IoTAdminHandler>();
            services.AddWebSocketManager<IoTDeviceHandler>();
        }
    }
}