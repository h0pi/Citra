namespace Citra.Api;

/// <summary>Names of the CORS policies (configured in Program.cs).</summary>
public static class CorsPolicies
{
    /// <summary>The public website-widget endpoints: any website may call them.</summary>
    public const string PublicWidget = "PublicWidget";
}

/// <summary>Names and limits of the rate-limiting policies (configured in Program.cs).</summary>
public static class RateLimits
{
    /// <summary>The public website-widget endpoints, per IP address.</summary>
    public const string PublicWidget = "PublicWidget";

    /// <summary>Loose enough for many visitors behind one office network; each visitor has a stricter limit.</summary>
    public const int PublicRequestsPerIpPerMinute = 60;

    /// <summary>Sign-up, login and token refresh, per IP address: stops password guessing across many accounts.</summary>
    public const string Auth = "Auth";

    /// <summary>Far above what people logging in need (an office behind one address included), far below an attack.</summary>
    public const int AuthRequestsPerIpPerMinute = 20;
}
