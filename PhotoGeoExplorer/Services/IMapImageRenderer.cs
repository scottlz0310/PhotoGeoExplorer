using System.IO;
using Mapsui;

namespace PhotoGeoExplorer.Services;

internal interface IMapImageRenderer
{
    Stream RenderPng(Map map, Viewport viewport, float pixelDensity);
}
