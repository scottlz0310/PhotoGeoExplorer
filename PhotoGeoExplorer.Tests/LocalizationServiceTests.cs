using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void GetStringWithNullKeyReturnsEmptyString()
    {
        var result = LocalizationService.GetString(null!);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetStringWithEmptyKeyReturnsEmptyString()
    {
        var result = LocalizationService.GetString(string.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetStringWithWhitespaceKeyReturnsEmptyString()
    {
        var result = LocalizationService.GetString("   ");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetStringWithValidKeyReturnsKeyInTestEnvironment()
    {
        // In test environment, ResourceManager is null, so the key is returned as-is
        var key = "TestKey";

        var result = LocalizationService.GetString(key);

        Assert.Equal(key, result);
    }

    [Fact]
    public void GetStringWithDottedKeyReturnsKeyInTestEnvironment()
    {
        // Tests that dotted keys are handled (normalized to slashes internally)
        var key = "MainWindow.Title";

        var result = LocalizationService.GetString(key);

        Assert.Equal(key, result);
    }

    [Fact]
    public void FormatWithNullKeyReturnsEmptyString()
    {
        var result = LocalizationService.Format(null!, "arg1");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FormatWithEmptyKeyReturnsEmptyString()
    {
        var result = LocalizationService.Format(string.Empty, "arg1");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FormatWithValidKeyAndArgsFormatsCorrectly()
    {
        // In test environment, key is returned as format string
        var key = "Hello {0}, you have {1} messages";

        var result = LocalizationService.Format(key, "User", 5);

        Assert.Equal("Hello User, you have 5 messages", result);
    }

    [Fact]
    public void FormatWithNoArgsReturnsKeyAsIs()
    {
        var key = "SimpleMessage";

        var result = LocalizationService.Format(key);

        Assert.Equal(key, result);
    }

    [Fact]
    public void FormatWithSingleArgFormatsCorrectly()
    {
        var key = "Welcome {0}!";

        var result = LocalizationService.Format(key, "World");

        Assert.Equal("Welcome World!", result);
    }

    [Fact]
    public async Task GetStringIsThreadSafe()
    {
        // Verifies that concurrent access doesn't throw exceptions
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var result = LocalizationService.GetString($"Key{index}");
                Assert.Equal($"Key{index}", result);
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [Fact]
    public async Task FormatIsThreadSafe()
    {
        // Verifies that concurrent Format calls don't throw exceptions
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var result = LocalizationService.Format("Message {0}", index);
                Assert.Equal($"Message {index}", result);
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [Fact]
    public void GetStringWithSpecialCharactersReturnsKey()
    {
        var key = "Key.With.Dots.And/Slashes";

        var result = LocalizationService.GetString(key);

        Assert.Equal(key, result);
    }

    [Fact]
    public void GetStringCalledMultipleTimesReturnsSameResult()
    {
        var key = "ConsistentKey";

        var result1 = LocalizationService.GetString(key);
        var result2 = LocalizationService.GetString(key);
        var result3 = LocalizationService.GetString(key);

        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }

    [Fact]
    public void FormatWithNumericArgsFormatsWithCurrentCulture()
    {
        var key = "Value: {0:N2}";

        var result = LocalizationService.Format(key, 1234.5678);

        // Result depends on current culture but should not throw
        Assert.NotNull(result);
        Assert.Contains("Value:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatWithDateArgsFormatsWithCurrentCulture()
    {
        var key = "Date: {0:d}";
        var date = new DateTime(2024, 1, 15);

        var result = LocalizationService.Format(key, date);

        // Result depends on current culture but should not throw
        Assert.NotNull(result);
        Assert.Contains("Date:", result, StringComparison.Ordinal);
    }
}
