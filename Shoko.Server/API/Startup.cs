﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using Sentry;
using Shoko.Server.API.ActionFilters;
using Shoko.Server.API.Authentication;
using Shoko.Server.API.SignalR;
using Shoko.Server.Plugin;
using Shoko.Server.Server;
using Shoko.Server.Settings;

namespace Shoko.Server.API
{
    public class Startup
    {
        private IWebHostEnvironment HostingEnvironment { get; }
        private IConfiguration Configuration { get; }
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public Startup(IWebHostEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath);

            HostingEnvironment = env;
            Configuration = builder.Build();
        }

        public void ConfigureServices(IServiceCollection services)
        {
            ShokoServer.ConfigureServices(services);
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CustomAuthOptions.DefaultScheme;
                options.DefaultChallengeScheme = CustomAuthOptions.DefaultScheme;
            }).AddScheme<CustomAuthOptions, CustomAuthHandler>(CustomAuthOptions.DefaultScheme, _ => { });

            services.AddAuthorization(auth =>
            {
                auth.AddPolicy("admin",
                    policy => policy.Requirements.Add(new UserHandler(user => user.IsAdmin == 1)));
                auth.AddPolicy("init",
                    policy => policy.Requirements.Add(new UserHandler(user =>
                        user.JMMUserID == 0 && user.UserName == "init")));
            });

            services.AddSwaggerGen(
                options =>
                {
                    // resolve the IApiVersionDescriptionProvider service
                    // note: that we have to build a temporary service provider here because one has not been created yet
                    var provider = services.BuildServiceProvider().GetRequiredService<IApiVersionDescriptionProvider>();

                    // add a swagger document for each discovered API version
                    // note: you might choose to skip or document deprecated API versions differently
                    foreach (var description in provider.ApiVersionDescriptions)
                    {
                        options.SwaggerDoc(description.GroupName, CreateInfoForApiVersion(description));
                    }

                    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme()
                    {
                        Description = "Shoko API Key Header",
                        Name = "apikey",
                        In = ParameterLocation.Header,
                        Type = SecuritySchemeType.ApiKey,
                        Scheme = "apikey",
                    });

                    options.AddSecurityRequirement(new OpenApiSecurityRequirement()
                    {
                        {
                            new OpenApiSecurityScheme {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.SecurityScheme,
                                    Id="ApiKey",
                                },
                            },
                            new string[]{}
                        },
                    });

                    // add a custom operation filter which sets default values
                    //options.OperationFilter<SwaggerDefaultValues>();

                    // integrate xml comments
                    //Locate the XML file being generated by ASP.NET...
                    var xmlFile = "Shoko.Server.xml";
                    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                    if (File.Exists(xmlPath)) options.IncludeXmlComments(xmlPath);

                    foreach (Type type in Loader.Instance.Plugins.Keys)
                    {
                        var assembly = type.Assembly;
                        var location = assembly.Location;
                        var xml = Path.Combine(Path.GetDirectoryName(location), $"{Path.GetFileNameWithoutExtension(location)}.xml");
                        if (File.Exists(xml)) options.IncludeXmlComments(xml); //Include the XML comments if it exists.
                    }
                    options.MapType<v3.Models.Shoko.SeriesType>(() => new OpenApiSchema { Type = "string" });
                    options.MapType<v3.Models.Shoko.EpisodeType>(() => new OpenApiSchema { Type = "string" });

                    options.CustomSchemaIds(x => x.FullName);
                });
            services.AddSwaggerGenNewtonsoftSupport();

            services.AddSignalR(o =>
            {
                o.EnableDetailedErrors = true;
            });

            services.AddSingleton<QueueEmitter>();
            services.AddSingleton<AniDBEmitter>();
            services.AddSingleton<LoggingEmitter>();
            services.AddSingleton<ShokoEventEmitter>();

            // allow CORS calls from other both local and non-local hosts
            services.AddCors(options =>
            {
                options.AddDefaultPolicy(
			        builder =>
                        {
                		builder
                		.AllowAnyOrigin()
                		.AllowAnyMethod()
                		.AllowAnyHeader();
            		});
            });

            // this caused issues with auth. https://stackoverflow.com/questions/43574552
            var mvc = services.AddMvc(options =>
                {
                    options.EnableEndpointRouting = false;
                    options.AllowEmptyInputInBodyModelBinding = true;
                    foreach (var formatter in options.InputFormatters)
                    {
                        if (formatter.GetType() == typeof(NewtonsoftJsonInputFormatter))
                            ((NewtonsoftJsonInputFormatter)formatter).SupportedMediaTypes.Add(
                                MediaTypeHeaderValue.Parse("text/plain"));
                    }

                    options.Filters.Add(typeof(DatabaseBlockedFilter));
                    options.Filters.Add(typeof(ServerNotRunningFilter));

                    EmitEmptyEnumerableInsteadOfNullAttribute.MvcOptions = options;
                })
                .SetCompatibilityVersion(CompatibilityVersion.Version_3_0)
                .AddNewtonsoftJson(json =>
                {
                    json.SerializerSettings.MaxDepth = 10;
                    json.SerializerSettings.ContractResolver = new DefaultContractResolver
                    {
                        NamingStrategy = new DefaultNamingStrategy()
                    };
                    json.SerializerSettings.NullValueHandling = NullValueHandling.Include;
                    json.SerializerSettings.DefaultValueHandling = DefaultValueHandling.Populate;
                    // json.SerializerSettings.DateFormatString = "yyyy-MM-dd";
                });

            foreach (Type type in Loader.Instance.Plugins.Keys)
            {
                var assembly = type.Assembly;
                if (assembly == Assembly.GetCallingAssembly()) continue; //Skip the current assembly, this is implicitly added by ASP.
                mvc.AddApplicationPart(assembly).AddControllersAsServices();
            }

            services.AddApiVersioning(o =>
            {
                o.ReportApiVersions = true;
                o.AssumeDefaultVersionWhenUnspecified = true;
                o.DefaultApiVersion = ApiVersion.Default;
                o.ApiVersionReader = ApiVersionReader.Combine(
                    new QueryStringApiVersionReader(),
                    new HeaderApiVersionReader("api-version"),
                    new ShokoApiReader()
                );
            });
            services.AddVersionedApiExplorer();
            services.AddResponseCaching();

            services.Configure<KestrelServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.Use(async (context, next) =>
            {
                try
                {
                    await next.Invoke();
                }
                catch (Exception e)
                {
                    SentrySdk.CaptureException(e);
                    Logger.Error(e);
                    throw;
                }
            });

#if DEBUG
            app.UseDeveloperExceptionPage();
#endif
            var dir = new DirectoryInfo(Path.Combine(ServerSettings.ApplicationPath, "webui"));
            if (!dir.Exists)
            {
                dir.Create();

                var backup = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
                    "webui"));
                if (backup.Exists) CopyFilesRecursively(backup, dir);
            }

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new WebUiFileProvider(dir.FullName),
                RequestPath = "/webui",
                ServeUnknownFileTypes = true
            });


            app.UseSwagger();
            app.UseSwaggerUI(
                options =>
                {
                    // build a swagger endpoint for each discovered API version
                    var provider = app.ApplicationServices.GetRequiredService<IApiVersionDescriptionProvider>();
                    foreach (var description in provider.ApiVersionDescriptions)
                    {
                        options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", description.GroupName.ToUpperInvariant());
                    }
                });
            // Important for first run at least
            app.UseAuthentication();

            app.UseRouting();
            app.UseEndpoints(conf =>
            {
                conf.MapHub<QueueHub>("/signalr/events");
                conf.MapHub<AniDBHub>("/signalr/anidb");
                conf.MapHub<LoggingHub>("/signalr/logging");
                conf.MapHub<ShokoEventHub>("/signalr/shoko");
            });

            app.UseCors(options => options.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

            app.UseMvc();
        }

        public static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target) {
            foreach (DirectoryInfo dir in source.GetDirectories())
                CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
            foreach (FileInfo file in source.GetFiles())
                file.CopyTo(Path.Combine(target.FullName, file.Name));
        }

        static OpenApiInfo CreateInfoForApiVersion(ApiVersionDescription description)
        {
            var info = new OpenApiInfo
            {
                Title = $"Shoko API {description.ApiVersion}",
                Version = description.ApiVersion.ToString(),
                Description = "Shoko Server API.",
            };

            if (description.IsDeprecated)
            {
                info.Description += " This API version has been deprecated.";
            }

            return info;
        }
    }

    internal class ShokoApiReader : IApiVersionReader
    {
        public void AddParameters(IApiVersionParameterDescriptionContext context)
        {
            context.AddParameter(null, ApiVersionParameterLocation.Path);
        }

        public string Read(HttpRequest request)
        {
            if (!string.IsNullOrEmpty(request.Headers["api-version"])) return null;
            if (!string.IsNullOrEmpty(request.Query["api-version"])) return null;

            PathString[] apiv1 =
            {
                "/v1", "/api/Image", "/api/Kodi",
                "/api/Metro", "/api/Plex", "/Stream"
            };

            PathString[] apiv2 =
            {
                "/api/webui", "/api/version", "/plex", "/api/init", "/api/dev", "/api/modules", "/api/core",
                "/api/links", "/api/cast", "/api/group", "/api/filter", "/api/cloud", "/api/serie", "/api/ep",
                "/api/file", "/api/queue", "/api/myid", "/api/news", "/api/search", "/api/remove_missing_files",
                "/api/stats_update", "/api/medainfo_update", "/api/hash", "/api/rescan", "/api/rescanunlinked",
                "/api/folder", "/api/rescanmanuallinks", "/api/rehash", "/api/config", "/api/rehashunlinked",
                "/api/rehashmanuallinks", "/api/ep"
            };

            if (apiv1.Any(request.Path.StartsWithSegments))
                return "1.0";

            if (apiv2.Any(request.Path.StartsWithSegments))
                return "2.0";

            return "2.0";//default to 2.0
        }
    }

    internal class WebUiFileProvider : PhysicalFileProvider, IFileProvider
    {
        public WebUiFileProvider(string root) : base(root)
        {
        }

        public new IDirectoryContents GetDirectoryContents(string subpath)
        {
            return base.GetDirectoryContents(subpath);
        }

        public new IFileInfo GetFileInfo(string subpath)
        {
            var fileInfo = base.GetFileInfo(subpath);
            if (fileInfo is NotFoundFileInfo || !fileInfo.Exists) return base.GetFileInfo("index.html");
            return fileInfo;
        }
    }

    /*
    public class SwaggerDefaultValues : IOperationFilter
    {
        /// <summary>
        /// Applies the filter to the specified operation using the given context.
        /// </summary>
        /// <param name="operation">The operation to apply the filter to.</param>
        /// <param name="context">The current operation filter context.</param>
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (operation.Parameters == null)
            {
                return;
            }

            // REF: https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/412
            // REF: https://github.com/domaindrivendev/Swashbuckle.AspNetCore/pull/413
            foreach (var parameter in operation.Parameters.OfType<Non>())
            {
                var description = context.ApiDescription.ParameterDescriptions.First(p => p.Name == parameter.Name);
                var routeInfo = description.RouteInfo;

                if (parameter.Description == null)
                {
                    parameter.Description = description.ModelMetadata?.Description;
                }

                if (routeInfo == null)
                {
                    continue;
                }

                if (parameter.Default == null)
                {
                    parameter.Default = routeInfo.DefaultValue;
                }

                parameter.Required |= !routeInfo.IsOptional;
            }
        }
    }
    */

    internal static class ApiExtensions
    {
        public static ApiVersionModel GetApiVersion(this ActionDescriptor actionDescriptor)
        {
            return actionDescriptor?.Properties
                .Where(kvp => (Type)kvp.Key == typeof(ApiVersionModel))
                .Select(kvp => kvp.Value as ApiVersionModel).FirstOrDefault();
        }
    }
}