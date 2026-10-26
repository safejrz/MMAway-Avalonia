using System.Text.Json;

namespace MMAway;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string settingsPath;

    public SettingsStore(string? settingsPath = null)
    {
        this.settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MMAway",
            "settings.json");
    }

    public async Task<ActivitySettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath)) return new ActivitySettings();

        await using var stream = File.OpenRead(settingsPath);
        var settings = await JsonSerializer.DeserializeAsync<ActivitySettings>(
            stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Settings file '{settingsPath}' is empty or invalid.");
        settings.Validate();
        return settings;
    }

    public async Task SaveAsync(ActivitySettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        var directory = Path.GetDirectoryName(settingsPath)
            ?? throw new InvalidOperationException("Settings path must include a directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = settingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, settingsPath, overwrite: true);
    }
}
