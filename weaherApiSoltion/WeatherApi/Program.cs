

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
// WeatherDbContext.seedDatabase();
            var builder = WebApplication.CreateBuilder(args);

            var t = (SqlConnection)new WeatherDbContext().Database.GetDbConnection();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();
            builder.Services.AddSingleton<SqlConnection>(_ => (SqlConnection)new WeatherDbContext().Database.GetDbConnection());
            builder.Services.AddDbContext<WeatherDbContext>();

// Create a Meter for custom metrics
            var meter = new Meter("MyAppMetrics", "1.0.0");
            var customCounter = meter.CreateCounter<long>("test_metric", "count", "A test metric that changes every 5 seconds.");
            ActivitySource activitySource = new ActivitySource("CustomTraceSource");
            builder.Services.AddSingleton<ActivitySource>(provider => activitySource);
            builder.Services.AddSingleton<MinimalApi>();

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
                            o.AgentHost = "localhost";
                            o.AgentPort = 6831; // Default port for Jaeger
                        });

                    //.AddConsoleExporter();

                });

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

            builder.Configuration
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddEnvironmentVariables() // Priority 1: Environment variables
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true); // Priority 2: appsettings.json


            var app = builder.Build();

//create the /metrics endpoint
            app.MapPrometheusScrapingEndpoint();



// Background task to update the metric every 5 seconds
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
