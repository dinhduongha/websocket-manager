using System;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bamboo.WebSocketManager
{
    public static class WebSocketManagerExtensions
    {
        public static IServiceCollection AddWebSocketManager(this IServiceCollection services, Assembly assembly = null)
        {
            services.AddTransient<WebSocketConnectionManager>();

            Assembly ass = assembly ?? Assembly.GetEntryAssembly();

            foreach (var type in ass.ExportedTypes)
            {
                if (type.GetTypeInfo().BaseType == typeof(WebSocketHandler))
                {
                    services.AddSingleton(type);
                }
            }

            return services;
        }
        public static IServiceCollection AddWebSocketManager<T>(this IServiceCollection services)
            where T: class, IWebSocketHandler
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddTransient<WebSocketConnectionManager>();
            services.AddSingleton<T>();
            return services;
        }

        public static IApplicationBuilder UseWebSocketManager(this IApplicationBuilder app,
                                                              PathString path,
                                                              WebSocketHandler handler)
        {
            return app.UseWhen(context => 
                               context.Request.Path.StartsWithSegments(path) && context.WebSockets.IsWebSocketRequest, 
                              (_app) => _app.UseMiddleware<WebSocketManagerMiddleware>(handler));
            //return app.Map(path, (_app) => _app.UseMiddleware<WebSocketManagerMiddleware>(handler));
        }
    }
}