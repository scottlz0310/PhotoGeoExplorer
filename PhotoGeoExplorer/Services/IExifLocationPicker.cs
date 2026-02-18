using System.Threading.Tasks;

namespace PhotoGeoExplorer.Services;

internal interface IExifLocationPicker
{
    bool CanPickExifLocation { get; }

    Task<(double Latitude, double Longitude)?> PickExifLocationAsync();
}
