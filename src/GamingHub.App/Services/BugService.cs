namespace GamingHub.App.Services;

/// <summary>Submits bug reports to the Supabase backend when configured.</summary>
public sealed class BugService
{
    public bool CloudAvailable => AppServices.Auth.Client is not null;

    public async Task<(bool ok, string message)> SubmitAsync(string title, string category, string steps)
    {
        var client = AppServices.Auth.Client;
        if (client is null)
            return (false, "not-configured");

        var user = AppServices.Auth.CurrentUser;
        var row = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["category"] = category,
            ["steps"] = steps,
            ["app_version"] = AppInfo.Version,
            ["reporter_email"] = string.IsNullOrWhiteSpace(user?.Email) ? null : user!.Email,
            ["reporter_id"] = string.IsNullOrWhiteSpace(user?.Id) ? null : user!.Id
        };

        var result = await client.InsertAsync("bugs", row);
        return result.Ok
            ? (true, "Danke! Dein Bugreport wurde übermittelt.")
            : (false, result.Body);
    }
}
