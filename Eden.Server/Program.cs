using Eden.Shared;
using Microsoft.AspNetCore.Server.Kestrel.Core;

// Enable WebTransport over HTTP/3. Must happen before WebApplication is built.
// When Eden.Viewer connects, it will speak WebTransport on this listener.
AppContext.SetSwitch(
    "Microsoft.AspNetCore.Server.Kestrel.Experimental.WebTransportAndH3Datagrams",
    true);

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    // 5001 listens for HTTP/1.1 + HTTP/2 + HTTP/3. H3 requires TLS; use the
    // ASP.NET Core dev cert (install once with `dotnet dev-certs https --trust`).
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
        listenOptions.UseHttps();
    });
});

var app = builder.Build();

app.MapGet("/", () => new
{
    product      = EdenVersion.Product,
    release      = EdenVersion.Release,
    wireProtocol = EdenVersion.WireProtocol,
});

app.Run();
