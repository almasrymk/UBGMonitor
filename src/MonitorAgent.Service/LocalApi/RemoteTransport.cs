using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace MonitorAgent.Service.LocalApi;
public static class RemoteTransport
{
    public static void Configure(WebApplicationBuilder builder, IPAddress address, int port, X509Certificate2 certificate)
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 8 * 1024 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            options.Listen(address, port, endpoint => endpoint.UseHttps(certificate));
        });
    }
}
