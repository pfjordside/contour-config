using System.Text.Json;

namespace ContourConfig.Core
{
    public sealed class SettingsStore(string path)
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            RespectNullableAnnotations = true
        };

        public async Task<DriftConfiguration?> LoadAsync(CancellationToken cancellationToken)
        {
            try
            {
                FileStream stream = File.OpenRead(path);
                await using (stream.ConfigureAwait(false))
                {
                    DriftConfiguration configuration = await JsonSerializer.DeserializeAsync<DriftConfiguration>(
                        stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException("The settings file contains no configuration.");
                    configuration.Validate();
                    return configuration with { Version = DriftConfiguration.CurrentVersion };
                }
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The settings file contains invalid JSON or incompatible values.", exception);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"The settings file contains invalid values: {exception.Message}", exception);
            }
        }

        public async Task SaveAsync(DriftConfiguration configuration, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            configuration.Validate();
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            string temporaryPath = fullPath + ".tmp";
            string json = JsonSerializer.Serialize(configuration, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
    }
}