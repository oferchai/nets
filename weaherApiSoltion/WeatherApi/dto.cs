using System;
using System.ComponentModel.DataAnnotations;

namespace WhratherApi.Dto
{
    
    public class WeatherRecord
    {
        public int Id { get; set; }
        public required string Location { get; set; }
        public double Temperature { get; set; }
        
        public DateTime Date { get; set; }

        [Timestamp]
        public required byte[] RowVersion { get; set; }
    }

    
    public record ApiCall {
        public string ActionDescription {get; set;}
    }

}