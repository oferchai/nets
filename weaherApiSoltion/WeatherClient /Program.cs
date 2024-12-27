using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Host Builder
using var host = Host.CreateDefaultBuilder()
    .ConfigureServices((context, services) =>
    {
        // Register HttpClient
        services.AddHttpClient("WeatherApiClient");
            /*
            .AddHttpMessageHandler(provider =>
                new TraceContextPropagationHandler()); // Custom handler for trace propagation
                */

        // Add OpenTelemetry Tracing
        services.AddOpenTelemetry().WithTracing( builder =>
        {
            builder.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("ConsoleApp"))
                .AddSource("ConsoleAppTraceSource")
                .AddHttpClientInstrumentation() // Trace HTTP calls
                .AddJaegerExporter(o =>
                {
                    o.AgentHost = "localhost"; // Adjust to your Jaeger host
                    o.AgentPort = 6831;        // Default Jaeger port
                });
        });

        // Add a worker service
        services.AddHostedService<WorkerService>();
    })
    .Build();

await host.RunAsync();

// Worker Service
public class WorkerService : IHostedService
{
    private readonly IHttpClientFactory _httpClientFactory;
    ActivitySource activitySource = new("ConsoleAppTraceSource", "1.0.0");
    public WorkerService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Get the HttpClient
        var client = _httpClientFactory.CreateClient("WeatherApiClient");

        while (cancellationToken.IsCancellationRequested == false)
        {
            // Start a trace span
            using (var activity = activitySource.StartActivity("CallMinimalApi", ActivityKind.Client))
            {
                string url = "http://localhost:5179/weather/weatherforecastByCity?city=rome";
                activity?.SetTag("custom.tag", "example");
                activity?.SetTag("http.method", "GET");
                activity?.SetTag("http.url", url);

                Console.WriteLine("Sending request to the minimal API...");

                // Make an HTTP request
                var response = await client.GetAsync(url, cancellationToken);

                Console.WriteLine($"Response Status Code: {response.StatusCode}");
            }
            
            await Task.Delay(5000, cancellationToken);
        }

        Console.WriteLine("Trace completed. Check Jaeger for trace details.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}


public class TraceContextPropagationHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // You can enrich headers or handle propagation here if necessary
        return await base.SendAsync(request, cancellationToken);
    }
}
