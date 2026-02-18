using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Services;

internal sealed class ExifEditorService : IExifEditorService
{
    private readonly IExifMetadataService _metadataService;
    private readonly IExifLocationPicker _locationPicker;
    private readonly Action<string, InfoBarSeverity> _showNotification;
    private readonly IExifEditDialogPresenter _dialogPresenter;

    public ExifEditorService(
        IDialogService dialogService,
        IExifMetadataService metadataService,
        IExifLocationPicker locationPicker,
        Action<string, InfoBarSeverity> showNotification)
        : this(dialogService, metadataService, locationPicker, showNotification, dialogPresenter: null)
    {
    }

    internal ExifEditorService(
        IDialogService dialogService,
        IExifMetadataService metadataService,
        IExifLocationPicker locationPicker,
        Action<string, InfoBarSeverity> showNotification,
        IExifEditDialogPresenter? dialogPresenter)
    {
        ArgumentNullException.ThrowIfNull(dialogService);
        _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        _locationPicker = locationPicker ?? throw new ArgumentNullException(nameof(locationPicker));
        _showNotification = showNotification ?? throw new ArgumentNullException(nameof(showNotification));
        _dialogPresenter = dialogPresenter ?? new ExifEditDialogPresenter(dialogService);
    }

    public Task<ExifEditValidationResult> ValidateExifEditableAsync(
        IReadOnlyList<PhotoListItem> selectedItems,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(selectedItems);

        if (selectedItems.Count != 1)
        {
            return Task.FromResult(ExifEditValidationResult.Invalid("Message.ExifEditorMultipleFiles"));
        }

        var item = selectedItems[0];
        if (item.IsFolder)
        {
            return Task.FromResult(ExifEditValidationResult.Invalid("Message.ExifEditorFolderSelected"));
        }

        return Task.FromResult(ExifEditValidationResult.Valid(item));
    }

    public async Task<bool> EditExifAsync(PhotoListItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = await _metadataService.GetMetadataAsync(item.FilePath, cancellationToken).ConfigureAwait(true);
        var state = new ExifEditState
        {
            UpdateDate = metadata?.TakenAt.HasValue ?? false,
            TakenAtDate = metadata?.TakenAt?.Date ?? DateTimeOffset.Now.Date,
            TakenAtTime = metadata?.TakenAt?.TimeOfDay ?? TimeSpan.Zero,
            LatitudeText = metadata?.Latitude?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            LongitudeText = metadata?.Longitude?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            UpdateFileDate = false
        };

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _dialogPresenter.ShowAsync(state, cancellationToken).ConfigureAwait(true);
            state = result.State;

            if (result.Action == ExifDialogAction.Cancel)
            {
                return false;
            }

            if (result.Action == ExifDialogAction.PickLocation)
            {
                var pickedLocation = await PickExifLocationAsync(cancellationToken).ConfigureAwait(true);
                if (pickedLocation is not null)
                {
                    state.LatitudeText = pickedLocation.Value.Latitude.ToString("F6", CultureInfo.InvariantCulture);
                    state.LongitudeText = pickedLocation.Value.Longitude.ToString("F6", CultureInfo.InvariantCulture);
                }

                continue;
            }

            break;
        }

        DateTimeOffset? newTakenAt = null;
        if (state.UpdateDate)
        {
            newTakenAt = new DateTimeOffset(
                state.TakenAtDate.Date.Add(state.TakenAtTime),
                DateTimeOffset.Now.Offset);
        }

        var newLatitude = TryParseCoordinate(state.LatitudeText);
        var newLongitude = TryParseCoordinate(state.LongitudeText);

        var success = await _metadataService.UpdateMetadataAsync(
            item.FilePath,
            newTakenAt,
            newLatitude,
            newLongitude,
            state.UpdateFileDate,
            cancellationToken).ConfigureAwait(true);

        if (success)
        {
            _showNotification(LocalizationService.GetString("Message.ExifUpdateSuccess"), InfoBarSeverity.Success);
            return true;
        }

        _showNotification(LocalizationService.GetString("Message.ExifUpdateFailed"), InfoBarSeverity.Error);
        return false;
    }

    private async Task<(double Latitude, double Longitude)?> PickExifLocationAsync(CancellationToken cancellationToken)
    {
        if (!_locationPicker.CanPickExifLocation)
        {
            _showNotification(LocalizationService.GetString("Message.ExifPickLocationUnavailable"), InfoBarSeverity.Warning);
            return null;
        }

        _showNotification(LocalizationService.GetString("Message.ExifPickLocationInstruction"), InfoBarSeverity.Informational);

        var pickedLocation = await _locationPicker.PickExifLocationAsync().ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();

        if (pickedLocation is null)
        {
            _showNotification(LocalizationService.GetString("Message.ExifPickLocationCanceled"), InfoBarSeverity.Informational);
            return null;
        }

        _showNotification(string.Empty, InfoBarSeverity.Informational);
        return pickedLocation;
    }

    private static double? TryParseCoordinate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}

internal interface IExifEditDialogPresenter
{
    Task<ExifEditDialogResult> ShowAsync(ExifEditState state, CancellationToken cancellationToken);
}

internal sealed class ExifEditDialogPresenter : IExifEditDialogPresenter
{
    private readonly IDialogService _dialogService;

    public ExifEditDialogPresenter(IDialogService dialogService)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    }

    public async Task<ExifEditDialogResult> ShowAsync(ExifEditState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var pickLocationRequested = false;
        var dialogContent = new StackPanel
        {
            Spacing = 12,
            MinWidth = 400
        };

        var updateDateCheckBox = new CheckBox
        {
            Content = LocalizationService.GetString("ExifEditor.UpdateDateCheckbox"),
            IsChecked = state.UpdateDate
        };
        AutomationProperties.SetAutomationId(updateDateCheckBox, "ExifEditor.UpdateDateCheckBox");
        dialogContent.Children.Add(updateDateCheckBox);

        var updateFileDateCheckBox = new CheckBox
        {
            Content = LocalizationService.GetString("ExifEditor.UpdateFileDate"),
            IsChecked = state.UpdateDate && state.UpdateFileDate,
            IsEnabled = state.UpdateDate
        };
        AutomationProperties.SetAutomationId(updateFileDateCheckBox, "ExifEditor.UpdateFileDateCheckBox");

        var takenAtLabel = new TextBlock
        {
            Text = LocalizationService.GetString("ExifEditor.TakenAtLabel"),
            FontWeight = FontWeights.SemiBold
        };
        var takenAtPicker = new DatePicker
        {
            Date = state.TakenAtDate,
            IsEnabled = state.UpdateDate
        };
        AutomationProperties.SetAutomationId(takenAtPicker, "ExifEditor.TakenAtDatePicker");
        var takenAtTimePicker = new TimePicker
        {
            Time = state.TakenAtTime,
            IsEnabled = state.UpdateDate
        };
        AutomationProperties.SetAutomationId(takenAtTimePicker, "ExifEditor.TakenAtTimePicker");

        updateDateCheckBox.Checked += (_, _) =>
        {
            takenAtPicker.IsEnabled = true;
            takenAtTimePicker.IsEnabled = true;
            updateFileDateCheckBox.IsEnabled = true;
        };
        updateDateCheckBox.Unchecked += (_, _) =>
        {
            takenAtPicker.IsEnabled = false;
            takenAtTimePicker.IsEnabled = false;
            updateFileDateCheckBox.IsChecked = false;
            updateFileDateCheckBox.IsEnabled = false;
        };

        dialogContent.Children.Add(takenAtLabel);
        dialogContent.Children.Add(takenAtPicker);
        dialogContent.Children.Add(takenAtTimePicker);

        var latitudeLabel = new TextBlock
        {
            Text = LocalizationService.GetString("ExifEditor.LatitudeLabel"),
            FontWeight = FontWeights.SemiBold
        };
        var latitudeBox = new TextBox
        {
            PlaceholderText = "0.0",
            Text = state.LatitudeText ?? string.Empty
        };
        AutomationProperties.SetAutomationId(latitudeBox, "ExifEditor.LatitudeTextBox");
        dialogContent.Children.Add(latitudeLabel);
        dialogContent.Children.Add(latitudeBox);

        var longitudeLabel = new TextBlock
        {
            Text = LocalizationService.GetString("ExifEditor.LongitudeLabel"),
            FontWeight = FontWeights.SemiBold
        };
        var longitudeBox = new TextBox
        {
            PlaceholderText = "0.0",
            Text = state.LongitudeText ?? string.Empty
        };
        AutomationProperties.SetAutomationId(longitudeBox, "ExifEditor.LongitudeTextBox");
        dialogContent.Children.Add(longitudeLabel);
        dialogContent.Children.Add(longitudeBox);

        ContentDialog dialog = null!;

        var getLocationButton = new Button
        {
            Content = LocalizationService.GetString("ExifEditor.GetLocationFromMap"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(getLocationButton, "ExifEditor.GetLocationButton");
        getLocationButton.Click += (_, _) =>
        {
            pickLocationRequested = true;
            CaptureState();
            dialog.Hide();
        };
        dialogContent.Children.Add(getLocationButton);

        var clearLocationButton = new Button
        {
            Content = LocalizationService.GetString("ExifEditor.ClearLocation"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(clearLocationButton, "ExifEditor.ClearLocationButton");
        clearLocationButton.Click += (_, _) =>
        {
            latitudeBox.Text = string.Empty;
            longitudeBox.Text = string.Empty;
        };
        dialogContent.Children.Add(clearLocationButton);
        dialogContent.Children.Add(updateFileDateCheckBox);

        dialog = new ContentDialog
        {
            Title = LocalizationService.GetString("ExifEditor.Title"),
            Content = dialogContent,
            PrimaryButtonText = LocalizationService.GetString("ExifEditor.SaveButton"),
            SecondaryButtonText = LocalizationService.GetString("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await _dialogService.ShowContentDialogAsync(dialog, cancellationToken).ConfigureAwait(true);
        CaptureState();

        if (pickLocationRequested)
        {
            return new ExifEditDialogResult(ExifDialogAction.PickLocation, state);
        }

        return result == ContentDialogResult.Primary
            ? new ExifEditDialogResult(ExifDialogAction.Save, state)
            : new ExifEditDialogResult(ExifDialogAction.Cancel, state);

        void CaptureState()
        {
            state.UpdateDate = updateDateCheckBox.IsChecked ?? false;
            state.TakenAtDate = takenAtPicker.Date;
            state.TakenAtTime = takenAtTimePicker.Time;
            state.LatitudeText = latitudeBox.Text ?? string.Empty;
            state.LongitudeText = longitudeBox.Text ?? string.Empty;
            state.UpdateFileDate = updateFileDateCheckBox.IsChecked ?? false;
        }
    }
}

internal sealed class ExifEditDialogResult
{
    public ExifEditDialogResult(ExifDialogAction action, ExifEditState state)
    {
        Action = action;
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public ExifDialogAction Action { get; }

    public ExifEditState State { get; }
}

internal sealed class ExifEditState
{
    public bool UpdateDate { get; set; }

    public DateTimeOffset TakenAtDate { get; set; }

    public TimeSpan TakenAtTime { get; set; }

    public string LatitudeText { get; set; } = string.Empty;

    public string LongitudeText { get; set; } = string.Empty;

    public bool UpdateFileDate { get; set; }
}

internal enum ExifDialogAction
{
    Save,
    Cancel,
    PickLocation
}
