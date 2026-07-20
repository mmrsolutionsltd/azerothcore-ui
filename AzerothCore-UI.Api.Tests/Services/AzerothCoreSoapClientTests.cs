using System.Net;
using System.Text;
using System.Xml.Linq;
using AzerothCore_UI.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class AzerothCoreSoapClientTests
{
    [Theory]
    [InlineData("Thrall")]
    [InlineData("Ab")]
    [InlineData("TwelveLetter")]
    public void RequirePlayerName_AcceptsValidNames(string value) =>
        Assert.Equal(value, AzerothCoreSoapClient.RequirePlayerName(value));

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Name1")]
    [InlineData("Name WithSpace")]
    [InlineData("ThirteenCharsz")]
    public void RequirePlayerName_RejectsUnsafeNames(string value) =>
        Assert.Throws<ArgumentException>(() => AzerothCoreSoapClient.RequirePlayerName(value));

    [Theory]
    [InlineData("Stormwind")]
    [InlineData("Acherus: The Ebon Hold", false)]
    [InlineData("Light's Hope Chapel")]
    [InlineData("Area_52")]
    public void RequireLocation_ValidatesAllowedCharacters(string value, bool valid = true)
    {
        if (valid) Assert.Equal(value, AzerothCoreSoapClient.RequireLocation(value));
        else Assert.Throws<ArgumentException>(() => AzerothCoreSoapClient.RequireLocation(value));
    }

    [Theory]
    [InlineData("Admin123")]
    [InlineData("ab", false)]
    [InlineData("admin-name", false)]
    public void RequireAccountName_ValidatesAccountNames(string value, bool valid = true)
    {
        if (valid) Assert.Equal(value, AzerothCoreSoapClient.RequireAccountName(value));
        else Assert.Throws<ArgumentException>(() => AzerothCoreSoapClient.RequireAccountName(value));
    }

    [Theory]
    [InlineData("http://127.0.0.1:7878", "admin", "secret", true)]
    [InlineData("http://localhost:7878", "admin", "secret", true)]
    [InlineData("http://example.com:7878", "admin", "secret", false)]
    [InlineData("not-a-uri", "admin", "secret", false)]
    [InlineData("http://127.0.0.1:7878", "", "secret", false)]
    public void IsConfigured_RequiresLoopbackEndpointAndCredentials(
        string endpoint, string username, string password, bool expected)
    {
        var client = CreateClient(endpoint, username, password, new StubHandler());
        Assert.Equal(expected, client.IsConfigured);
    }

    [Fact]
    public async Task ExecuteAsync_SendsAuthenticatedSoapEnvelopeAndReturnsResult()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "<Envelope><Body><executeCommandResponse><result>Command output</result></executeCommandResponse></Body></Envelope>",
                Encoding.UTF8, "text/xml")
        });
        var client = CreateClient("http://127.0.0.1:7878", "soapuser", "soappass", handler);

        var result = await client.ExecuteAsync("server info", CancellationToken.None);

        Assert.Equal("Command output", result);
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        Assert.Equal("Basic", handler.Request.Headers.Authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("soapuser:soappass")),
            handler.Request.Headers.Authorization?.Parameter);
        var envelope = XDocument.Parse(handler.Body);
        Assert.Equal("server info", envelope.Descendants().Single(element => element.Name.LocalName == "command").Value);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsSoapFault()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<Envelope><Body><Fault><faultstring>Command denied</faultstring></Fault></Body></Envelope>")
        });
        var client = CreateClient("http://127.0.0.1:7878", "admin", "secret", handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ExecuteAsync("server info", CancellationToken.None));

        Assert.Contains("Command denied", exception.Message);
    }

    private static AzerothCoreSoapClient CreateClient(
        string endpoint, string username, string password, StubHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzerothCore:Soap:Endpoint"] = endpoint,
            ["AzerothCore:Soap:Username"] = username,
            ["AzerothCore:Soap:Password"] = password
        }).Build();
        return new AzerothCoreSoapClient(configuration, new StubHttpClientFactory(handler));
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage>? responseFactory = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<Envelope><Body><result>OK</result></Body></Envelope>")
            };
        }
    }
}
