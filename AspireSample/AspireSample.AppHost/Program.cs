var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");
var sql = builder.AddSqlServer("sql").WithImage("azure-sql-edge")
    .WithLifetime(ContainerLifetime.Persistent);

var db = sql.AddDatabase("database");

var apiService = builder.AddProject<Projects.AspireSample_ApiService>("apiservice")
    .WithReference(db)
    .WaitFor(db)
    .WithReplicas(1);

builder.AddProject<Projects.AspireSample_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithReplicas(2);

builder.Build().Run();
