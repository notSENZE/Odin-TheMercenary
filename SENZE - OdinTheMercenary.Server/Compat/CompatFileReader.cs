using System.Text.Json;
using SPTarkov.DI.Annotations;

namespace TheMercenaryServer.Compat;

[Injectable(InjectionType.Singleton)]
public sealed class CompatFileReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public async Task<CompatRuleSet> ReadAsync(string file)
    {
        try
        {
            var raw = await File.ReadAllTextAsync(file);
            return JsonSerializer.Deserialize<CompatRuleSet>(raw, Options) ?? new CompatRuleSet();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Could not read compat file '{System.IO.Path.GetFileName(file)}': {exception.Message}",
                exception);
        }
    }
}
