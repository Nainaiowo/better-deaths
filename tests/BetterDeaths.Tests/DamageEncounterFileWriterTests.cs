using System.Text;
using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class DamageEncounterFileWriterTests
{
    [Fact]
    public void StreamingSavePreservesExactJsonBytesAndReplacesExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"better-deaths-encounter-{Guid.NewGuid():N}.json");
        var options = new JsonSerializerOptions { WriteIndented = false };
        var data = new { Name = "Player <one>", Amount = 12345.6789, Events = Enumerable.Range(0, 20000).ToArray() };
        try
        {
            File.WriteAllText(path, "old history");
            DamageEncounterFileWriter.Write(path, data, options);
            Assert.Equal(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, options)), File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }

    [Fact]
    public void FailedSerializationLeavesThePreviousFileIntactAndTheNextSaveCanSucceed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"better-deaths-encounter-{Guid.NewGuid():N}.json");
        var options = new JsonSerializerOptions();
        try
        {
            File.WriteAllText(path, "previous complete encounter");
            Assert.Throws<InvalidOperationException>(() => DamageEncounterFileWriter.Write(path, new FailingData(), options));
            Assert.Equal("previous complete encounter", File.ReadAllText(path));
            DamageEncounterFileWriter.Write(path, new { Amount = 1000 }, options);
            Assert.Equal("{\"Amount\":1000}", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }

    private sealed class FailingData
    {
        public int Amount => throw new InvalidOperationException("Simulated serialization failure");
    }
}
