using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AzerothCore_UI.Api.Services;

public sealed class AzerothCoreSoapClient(IConfiguration configuration, IHttpClientFactory httpClientFactory)
{
    private readonly string? endpoint = configuration["AzerothCore:Soap:Endpoint"];
    private readonly string? username = configuration["AzerothCore:Soap:Username"];
    private readonly string? password = configuration["AzerothCore:Soap:Password"];

    public bool IsConfigured => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && uri.IsLoopback && !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);

    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsConfigured) throw new InvalidOperationException("Local AzerothCore SOAP access is not configured.");

            var envelope = new XDocument(
                new XElement(XName.Get("Envelope", "http://schemas.xmlsoap.org/soap/envelope/"),
                    new XAttribute(XNamespace.Xmlns + "ns1", "urn:AC"),
                    new XElement(XName.Get("Body", "http://schemas.xmlsoap.org/soap/envelope/"),
                        new XElement(XName.Get("executeCommand", "urn:AC"),
                            new XElement("command", command)))));

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
            request.Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");

            var client = httpClientFactory.CreateClient(nameof(AzerothCoreSoapClient));
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            XDocument? document = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try { document = XDocument.Parse(body); }
                catch (System.Xml.XmlException) when (!response.IsSuccessStatusCode) { }
            }

            var fault = document?.Descendants().FirstOrDefault(element => element.Name.LocalName == "faultstring");
            if (fault is not null) throw new InvalidOperationException(fault.Value);

            response.EnsureSuccessStatusCode();
            if (document is null) throw new InvalidOperationException("The worldserver returned an empty SOAP response.");

            return document.Descendants().FirstOrDefault(element => element.Name.LocalName == "result")?.Value
                ?? document.Descendants().FirstOrDefault(element => element.Name.LocalName == "executeCommandResponse")?.Value
                ?? "Command completed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"AzerothCore SOAP command failed: {exception.Message}", exception);
        }
    }

    public static string RequirePlayerName(string value) =>
        Regex.IsMatch(value, "^[A-Za-z]{2,12}$")
            ? value : throw new ArgumentException("Player names must contain 2 to 12 letters.");

    public static string RequireLocation(string value) =>
        Regex.IsMatch(value, "^[A-Za-z0-9 _'-]{1,64}$")
            ? value : throw new ArgumentException("The teleport location contains unsupported characters.");

    public static string BuildTeleportCommand(string playerName, string location) =>
        $"teleport name {RequirePlayerName(playerName)} {RequireLocation(location)}";

    public static string BuildTrainerTeleportCommand(string playerName, uint spawnId)
    {
        if (spawnId == 0) throw new ArgumentOutOfRangeException(nameof(spawnId), "Trainer spawn ID is required.");
        return $"teleport name npc guid {RequirePlayerName(playerName)} {spawnId}";
    }

    public static string BuildNpcTeleportCommand(string playerName, uint spawnId, bool allowHostile)
    {
        if (spawnId == 0) throw new ArgumentOutOfRangeException(nameof(spawnId), "NPC spawn ID is required.");
        return $"webadmin npc teleport {RequirePlayerName(playerName)} {spawnId} {(allowHostile ? 1 : 0)}";
    }

    public static string BuildQuestCommand(string playerName, uint questId, bool add)
    {
        if (questId == 0) throw new ArgumentOutOfRangeException(nameof(questId), "Quest ID is required.");
        return $"quest {(add ? "add" : "remove")} {questId} {RequirePlayerName(playerName)}";
    }

    public static string BuildAuctionHouseSellerCommand(bool enabled) =>
        $"ahbotoptions seller {(enabled ? 1 : 0)}";

    public static string RequireAccountName(string value) =>
        Regex.IsMatch(value, "^[A-Za-z0-9]{3,32}$")
            ? value : throw new ArgumentException("Account names must contain 3 to 32 letters or numbers.");
}
