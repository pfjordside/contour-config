using Xunit;

namespace ContourConfig.Core.Tests.Unit
{
    public sealed class SettingsStoreTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), $"ContourConfigTests-{Guid.NewGuid():N}");

        [Fact]
        public async Task SaveAsync_NewStore_RestoresMarkersAndOptions()
        {
            string path = Path.Combine(directory, "settings.json");
            SettingsStore store = new(path);
            DriftConfiguration expected = new()
            {
                Options = new(800, 1600, 600, 20000),
                Markers = new(0x10, 0x20),
                EnableBoostOnStart = true
            };

            await store.SaveAsync(expected, TestContextToken);
            DriftConfiguration? actual = await new SettingsStore(path).LoadAsync(TestContextToken);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{\"Version\":99}")]
        [InlineData("{\"Options\":null}")]
        [InlineData("{\"Options\":{\"LeftAdditionalSpeed\":-1}}")]
        [InlineData("{\"Options\":{\"ActivationDelayMilliseconds\":-1}}")]
        [InlineData("{\"Markers\":{\"LeftMask\":8,\"RightMask\":8}}")]
        [InlineData("{\"Markers\":{\"LeftMask\":0}}")]
        public async Task LoadAsync_InvalidConfiguration_ReportsFailureInsteadOfUsingDefaults(string json)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, json, TestContextToken);

            await Assert.ThrowsAsync<InvalidDataException>(() => new SettingsStore(path).LoadAsync(TestContextToken));
        }

        [Fact]
        public async Task LoadAsync_Version1File_KeepsSpeedsAndUsesDefaultMarkers()
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.json");
            const string json = "{\"Version\":1,\"DevicePath\":\"old\",\"Options\":{\"LeftAdditionalSpeed\":5000,\"RightAdditionalSpeed\":4000,\"ActivationDelayMilliseconds\":450},\"Left\":null,\"Right\":{\"DeltaX\":3,\"IntervalMilliseconds\":20}}";
            await File.WriteAllTextAsync(path, json, TestContextToken);

            DriftConfiguration? result = await new SettingsStore(path).LoadAsync(TestContextToken);

            Assert.Equal(new DriftConfiguration { Options = new(5000, 4000, 450) }, result);
        }
        [Fact]
        public async Task LoadAsync_FirstRun_ReturnsNoSavedConfiguration()
        {
            SettingsStore store = new(Path.Combine(directory, "settings.json"));

            DriftConfiguration? result = await store.LoadAsync(TestContextToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task SaveAsync_CancelledUpdate_PreservesPreviouslySavedSettings()
        {
            SettingsStore store = new(Path.Combine(directory, "settings.json"));
            DriftConfiguration original = new() { Options = new(100, 100, 300) };
            await store.SaveAsync(original, TestContextToken);
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.SaveAsync(original with { Options = new(900, 900, 300) }, cancellation.Token));
            DriftConfiguration? restored = await store.LoadAsync(TestContextToken);

            Assert.Equal(original, restored);
        }

        // xUnit v2 has no runner-provided cancellation token.
        private static CancellationToken TestContextToken => CancellationToken.None;

        public void Dispose()
        {
            if (Directory.Exists(directory))
            {
                File.Delete(Path.Combine(directory, "settings.json"));
                File.Delete(Path.Combine(directory, "settings.json.tmp"));
                Directory.Delete(directory);
            }
        }
    }
}