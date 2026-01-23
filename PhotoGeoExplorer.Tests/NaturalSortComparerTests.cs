using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class NaturalSortComparerTests
{
    [Fact]
    public void CompareSortsNumbersNaturally()
    {
        // Arrange
        var input = new[] { "11", "1", "2", "3" };

        // Act
        var sorted = input.OrderBy(x => x, NaturalSortComparer.Instance).ToArray();

        // Assert
        Assert.Equal(["1", "2", "3", "11"], sorted);
    }

    [Fact]
    public void CompareSortsFileNamesWithNumbersNaturally()
    {
        // Arrange
        var input = new[] { "file10.jpg", "file2.jpg", "file1.jpg", "file20.jpg" };

        // Act
        var sorted = input.OrderBy(x => x, NaturalSortComparer.Instance).ToArray();

        // Assert
        Assert.Equal(["file1.jpg", "file2.jpg", "file10.jpg", "file20.jpg"], sorted);
    }

    [Fact]
    public void CompareSortsMixedAlphaNumeric()
    {
        // Arrange
        var input = new[] { "photo100", "photo10", "photo1", "photo2" };

        // Act
        var sorted = input.OrderBy(x => x, NaturalSortComparer.Instance).ToArray();

        // Assert
        Assert.Equal(["photo1", "photo2", "photo10", "photo100"], sorted);
    }

    [Fact]
    public void CompareHandlesEmptyStrings()
    {
        // Arrange
        var input = new[] { "b", "", "a" };

        // Act
        var sorted = input.OrderBy(x => x, NaturalSortComparer.Instance).ToArray();

        // Assert
        Assert.Equal(["", "a", "b"], sorted);
    }

    [Fact]
    public void CompareHandlesNullValues()
    {
        // Arrange & Act & Assert
        Assert.Equal(0, NaturalSortComparer.Instance.Compare(null, null));
        Assert.True(NaturalSortComparer.Instance.Compare(null, "a") < 0);
        Assert.True(NaturalSortComparer.Instance.Compare("a", null) > 0);
    }

    [Fact]
    public void CompareIsCaseInsensitive()
    {
        // Arrange
        var input = new[] { "File2", "FILE1", "file3" };

        // Act
        var sorted = input.OrderBy(x => x, NaturalSortComparer.Instance).ToArray();

        // Assert
        // Windows StrCmpLogicalW は大文字小文字を区別しない自然順ソートを行う
        Assert.Equal("FILE1", sorted[0]);
        Assert.Equal("File2", sorted[1]);
        Assert.Equal("file3", sorted[2]);
    }

    [Fact]
    public void CompareSortsJapaneseFileNames()
    {
        // Arrange
        var input = new[] { "写真10.jpg", "写真2.jpg", "写真1.jpg" };

        // Act
        var sorted = input.OrderBy(x => x, NaturalSortComparer.Instance).ToArray();

        // Assert
        Assert.Equal(["写真1.jpg", "写真2.jpg", "写真10.jpg"], sorted);
    }

    [Fact]
    public void CompareSortsWithLeadingZeros()
    {
        // Arrange
        var input = new[] { "file001", "file01", "file1", "file002" };

        // Act
        var sorted = input.OrderBy(x => x, NaturalSortComparer.Instance).ToArray();

        // Assert
        // Windows StrCmpLogicalW は先頭ゼロの数が少ないものを優先（桁数でグループ化）
        Assert.Equal(["file001", "file01", "file1", "file002"], sorted);
    }

    [Fact]
    public void CompareSortsMultipleNumberSegments()
    {
        // Arrange
        var input = new[] { "photo1-2", "photo1-10", "photo2-1", "photo1-1" };

        // Act
        var sorted = input.OrderBy(x => x, NaturalSortComparer.Instance).ToArray();

        // Assert
        Assert.Equal(["photo1-1", "photo1-2", "photo1-10", "photo2-1"], sorted);
    }

    [Fact]
    public void InstanceReturnsSameInstance()
    {
        // Act & Assert
        Assert.Same(NaturalSortComparer.Instance, NaturalSortComparer.Instance);
    }
}
