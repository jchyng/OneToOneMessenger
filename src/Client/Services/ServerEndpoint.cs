namespace OneToOneMessenger_Client.Services;

public static class ServerEndpoint
{
    private const string DefaultUrl = "http://localhost:5000";

    public static string Url
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("ONETOONE_MESSENGER_SERVER_URL");
            return Uri.TryCreate(configured, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.GetLeftPart(UriPartial.Authority)
                : DefaultUrl;
        }
    }
}
