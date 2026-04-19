using Eden.Shared;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => new
{
    product      = EdenVersion.Product,
    release      = EdenVersion.Release,
    wireProtocol = EdenVersion.WireProtocol,
});

app.Run();
