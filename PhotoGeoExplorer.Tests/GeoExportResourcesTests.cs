using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class GeoExportResourcesTests
{
    private static readonly string[] Keys =
    [
        "MenuFileExport.Text",
        "MenuFileExportCsv.Text",
        "MenuFileExportByArea.Text",
        "MenuFileExportRenamed.Text",
        "CancelExportButton.Content",
        "Menu.Export",
        "Menu.ExportCsv",
        "Menu.ExportByArea",
        "Menu.ExportRenamed",
        "GeoExport.NoDate",
        "GeoExport.NoRoad",
        "GeoExport.Message.RoadNameIncomplete",
        "GeoExport.Csv.RoadName",
        "GeoExport.Csv.RoadNameStatus",
        "GeoExport.Csv.RoadStatusConfirmed",
        "GeoExport.Csv.RoadStatusCandidate",
        "GeoExport.Csv.RoadStatusNone",
        "GeoExport.Progress.Loading",
        "GeoExport.Progress.Copying",
        "GeoExport.Message.CsvDone",
        "GeoExport.Message.CsvFailed",
        "GeoExport.Message.CopyDone",
        "GeoExport.Message.CopyFailed",
        "GeoExport.Message.CopyCanceled",
        "GeoExport.Message.Canceled",
        "GeoExport.Message.AreaDataFailed",
        "GeoExport.Csv.FileName",
        "GeoExport.Csv.Prefecture",
        "GeoExport.Csv.City",
        "GeoExport.Csv.Town",
        "GeoExport.Csv.KeyCode",
        "GeoExport.Csv.Latitude",
        "GeoExport.Csv.Longitude",
        "GeoExport.Csv.TakenAt",
        "GeoExport.Csv.Status",
        "GeoExport.Csv.StatusResolved",
        "GeoExport.Csv.StatusWater",
        "GeoExport.Csv.StatusNoAreaData",
        "GeoExport.Csv.StatusNoLocation"
    ];

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("en-US")]
    public void EveryExportTextExistsInTheLanguage(string language)
    {
        var path = Path.Combine(RepositoryPaths.FindRoot(), "PhotoGeoExplorer", "Strings", language, "Resources.resw");
        var available = XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(element => (string)element.Attribute("name")!, element => (string?)element.Element("value"), StringComparer.Ordinal);

        Assert.All(Keys, key =>
        {
            Assert.True(available.ContainsKey(key), $"{language} の Resources.resw に {key} がありません。");
            Assert.False(string.IsNullOrWhiteSpace(available[key]), $"{language} の {key} が空です。");
        });
    }

    [Theory]
    [InlineData("GeoExport.Progress.Loading", 2)]
    [InlineData("GeoExport.Progress.Copying", 2)]
    [InlineData("GeoExport.Message.CsvDone", 2)]
    [InlineData("GeoExport.Message.CsvFailed", 1)]
    [InlineData("GeoExport.Message.CopyDone", 2)]
    [InlineData("GeoExport.Message.CopyFailed", 3)]
    [InlineData("GeoExport.Message.CopyCanceled", 3)]
    public void FormatTextsHaveTheSamePlaceholdersInBothLanguages(string key, int placeholderCount)
    {
        foreach (var language in new[] { "ja-JP", "en-US" })
        {
            var path = Path.Combine(RepositoryPaths.FindRoot(), "PhotoGeoExplorer", "Strings", language, "Resources.resw");
            var value = XDocument.Load(path).Root!.Elements("data")
                .Single(element => (string)element.Attribute("name")! == key)
                .Element("value")!.Value;

            for (var i = 0; i < placeholderCount; i++)
            {
                Assert.Contains("{" + i + "}", value, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("{" + placeholderCount + "}", value, StringComparison.Ordinal);
        }
    }
}
