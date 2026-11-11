using Citra.Application.Features.Auth.Register;

namespace Citra.Infrastructure.Options;

/// <summary>Who may sign up (section "Registration").</summary>
public sealed class RegistrationOptions : IRegistrationSettings
{
    public const string SectionName = "Registration";

    /// <summary>
    /// False on a public showcase server: nobody can create a company (and spend the AI services' budget on
    /// it); visitors use "Try the demo" instead. Existing accounts keep logging in.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
