using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Delta;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using WhratherApi.Dto;

public  class MinimalApi
{
    private readonly ActivitySource _activitySource;

    public  MinimalApi(ActivitySource activitySource)
    {
        _activitySource = activitySource;
    }
    public void buildApiMap(WebApplication webApplication)
    {
        var grp = webApplication.MapGroup("weather").UseDelta();
        grp.MapGet("/weatherforecast",async () =>
            {

                var forecast = new List<WeatherRecord>();
                using var activity = _activitySource.StartActivity("CustomTraceExample");
                activity?.SetTag("uri", "weatherforecast");

                await using var context = new WeatherDbContext();
                forecast = context.WeatherRecords.ToList();
                if( activity!=null) Console.WriteLine($"Adding trace {activity.TraceId}");
                activity?.AddEvent(new ActivityEvent("Trace operation completed"));


                return forecast;
            })
            .WithName("GetWeatherForecast");

        grp.MapGet("/weatherforecastByCity", async (string? city, WeatherDbContext ctx) =>
        {
            using var activity = _activitySource.StartActivity("CustomTraceExample");
            activity?.SetTag("uri", "weatherforecastByCity");

            using var context = new WeatherDbContext();
            var forecast = await ctx.WeatherRecords.Where(w => EF.Functions.Like(w.Location, $"%{city}%")).ToListAsync();
            if( activity!=null) Console.WriteLine($"    Adding trace {activity.TraceId}");
            activity?.AddEvent(new ActivityEvent("Trace operation completed"));
                
            return forecast;
        });

        grp.MapPost("/changeByCity", async (string? city, WeatherDbContext ctx, IBus bus) =>
        {


            // Retrieve 5 random records based on the city
            var forecast = await ctx.WeatherRecords
                .Where(w => EF.Functions.Like(w.Location, $"%{city}%"))
                .OrderBy(_ => Guid.NewGuid()) // Randomize the order
                .Take(2) // Select top 5 random records
                .ToListAsync();

            // Modify the records as needed
            foreach (var record in forecast)
            {
                record.Temperature = 25; // Example modification, set the temperature to 25
                record.Date = DateTime.Now; // Example: update the updated timestamp
            }

            // Save changes to the database
            await ctx.SaveChangesAsync();
            await bus.Publish(new ApiCall { ActionDescription = $@"change city:{city}" });



        });
    }


    record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
    {
        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    }

}