using Champ.TCPHealthCheck;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<TCPHealthProbe>();

var host = builder.Build();
host.Run();