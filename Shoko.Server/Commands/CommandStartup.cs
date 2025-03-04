﻿using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Shoko.Server.Commands.Interfaces;

namespace Shoko.Server.Commands
{
    public static class CommandStartup
    {
        public static void Configure(IServiceCollection services)
        {
            services.AddSingleton<ICommandRequestFactory, CommandRequestFactory>();
            
            // Register Requests
            var requestType = typeof(ICommandRequest);
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(p => requestType.IsAssignableFrom(p) && !p.IsAbstract && p.IsClass);

            foreach (var type in types)
            {
                services.AddTransient(type);
            }
        }
    }
}
