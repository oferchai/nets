using System;
using System.Linq;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using WhratherApi.Dto;

public class WeatherDbContext : DbContext
{
    public DbSet<WeatherRecord> WeatherRecords { get; set; }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);
        /*mb.AddInboxStateEntity();
        mb.AddOutboxMessageEntity();
        mb.AddOutboxMessageEntity();*/
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlServer("Server=localhost,9000;Database=tests;User Id=sa;Password=Test@123;TrustServerCertificate=True;");

    public static void seedDatabase() 
    {
        

        using var context = new WeatherDbContext();
        context.Database.EnsureCreated();

        var cities = new[]
        {
            "Copenhagen", "TelAviv", "Brussel", "Amsterdam", "Rome", "NYC", "Bangkok", "KhoPhangen", "Berlin", "Haifa"
        };

        var rnd = new Random();
       for (int i = 0; i<1000 ; i++)
       {
            context.WeatherRecords.Add(new WeatherRecord
            {
                Location = cities[rnd.Next(0,cities.Count()-1)],
                Temperature = rnd.NextDouble(),
                Date = DateTime.Now, 
                RowVersion = null
            });
            
        }
        context.SaveChanges();

        Console.WriteLine("Database initialized and seeded!");
    }

}
