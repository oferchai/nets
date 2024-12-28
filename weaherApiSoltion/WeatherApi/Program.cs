

using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

namespace WaetherApi
{


    public static class Program
    {

        public static void Main( string[] args )
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Configuration
                .SetBasePath(Directory.GetCurrentDirectory())

                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true) // Priority 2: appsettings.json
                .AddEnvironmentVariables();
            
            
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                                   ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            Console.WriteLine($"using connectionString: {connectionString}");
            // Register DbContext with the configured connection string
            builder.Services.AddDbContext<WeatherDbContext>(options =>
                options.UseSqlServer(connectionString));
            builder.Services.AddOpenApi();
            builder.Services.AddSingleton<SqlConnection>(_ => new SqlConnection(connectionString));
            //builder.Services.AddDbContext<WeatherDbContext>();

            
            builder.Services.AddSingleton<MinimalApi>();

            AddOpenTel(builder);

            AddMassTrans(builder);

           

            var app = builder.Build();

            // Manually create a scope and seed the database
            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<WeatherDbContext>();
                dbContext.seedDatabase(); // Pass the dbContext to the seed method
            }
            
            
            
            //create the /metrics endpoint
            app.MapPrometheusScrapingEndpoint();



        AddTimerFunction();

        // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.MapScalarApiReference();
            }

            app.UseHttpsRedirection();
            app.UseMiddleware<TraceIdMiddleware>();


            app.Services.GetRequiredService<MinimalApi>().buildApiMap(app);



            app.Run();

            
        }

        private static void AddTimerFunction()
        {
            // Background task to update the metric every 5 seconds
            var meter = new Meter("MyAppMetrics", "1.0.0");
            var customCounter = meter.CreateCounter<long>("test_metric", "count", "A test metric that changes every 5 seconds.");

            var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            _ = Task.Run(async () =>
            {
                var random = new Random();
                while (await timer.WaitForNextTickAsync())
                {
                    var incrementValue = random.Next(1, 10); // Simulate a random change
                    customCounter.Add(incrementValue);
                    Console.WriteLine($"Updated test_metric by {incrementValue}");
                }
            });
        }

        private static void AddMassTrans(WebApplicationBuilder builder)
        {
            builder.Services.AddMassTransit(x =>
            {
                // Use Kafka Rider for message publishing
                x.AddRider(rider =>
                {
                    rider.UsingKafka((context, kafka) =>
                    {
                        kafka.Host("localhost:9092"); // Specify Kafka broker
                    });
                });
                x.UsingInMemory((context, cfg) => { cfg.ConfigureEndpoints(context); });

                //x.AddEntityFrameworkOutbox<WeatherDbContext>( o=>o.UseSqlServer() );

            });
        }

        private static void AddOpenTel(WebApplicationBuilder builder)
        {
            // Create a Meter for custom metrics
            ActivitySource activitySource = new ActivitySource("CustomTraceSource");
            builder.Services.AddSingleton<ActivitySource>(provider => activitySource);
            var jaegerConfig = builder.Configuration.GetSection("Jaeger").Get<JaegerConfig>();

            // Check if Jaeger settings are available
            if (jaegerConfig == null)
            {
                throw new InvalidOperationException("Jaeger configuration is missing in appsettings.json.");
            }
            
            Console.WriteLine($"Using Jaeger config: {jaegerConfig.AgentHost}:{jaegerConfig.AgentPort}");
            
            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics =>
                {
                    metrics.AddAspNetCoreInstrumentation(); // Tracks ASP.NET Core metrics
                    metrics.AddHttpClientInstrumentation(); // Tracks HTTP client metrics
                    metrics.AddMeter("MyAppMetrics");
                    metrics.AddPrometheusExporter();
                    // Add EF Core instrumentation
                    metrics.AddPrometheusHttpListener(options =>
                    {
                        //options...StartHttpListener = true;
                        options.UriPrefixes = new[] { "http://localhost:9184/" }; // Exposes metrics at port 9464
                    });
                })
                .WithTracing(tracerProviderBuilder =>
                {
                    tracerProviderBuilder
                        .SetResourceBuilder(ResourceBuilder.CreateDefault()
                            .AddService("WeatherForecastService"))
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddSource("CustomTraceSource")
                        .AddEntityFrameworkCoreInstrumentation()
                        .AddSqlClientInstrumentation(option =>
                            {
                                option.RecordException = true;
                                option.SetDbStatementForText = true;
                                option.EnableConnectionLevelAttributes = true;
                            }
                        )
                        .AddJaegerExporter(o =>
                        {
                            o.AgentHost = jaegerConfig.AgentHost;
                            o.AgentPort = jaegerConfig.AgentPort; // Default port for Jaeger
                        });

                    //.AddConsoleExporter();

                });
        }
    }
    
    public class JaegerConfig
    {
        public string AgentHost { get; set; }
        public int AgentPort { get; set; }
    }


    public class TraceIdMiddleware
    {
        private readonly RequestDelegate _next;

        public TraceIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Check if there's an active trace
            var activity = System.Diagnostics.Activity.Current;
            if (activity != null)
            {
                // Add X-Trace-Id to the headers before passing to the next middleware
                //context.Response.Headers["X-trace-id"] = activity.TraceId.ToString();
                //context.Response.Headers["X-trace-state"] = activity.TraceStateString;
                context.Response.Headers["traceparent"] = activity.Id;
            }

            // Call the next middleware
            await _next(context);
        }
        
        
    }
}
