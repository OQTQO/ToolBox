using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;

namespace ToolBox.Host;

public sealed partial class PluginWorkspaceViewModel
{
    private string? _lastShownDialogId;

    private void OnUiSnapshotUpdated(
        object? sender,
        PluginUiSnapshotUpdatedEventArgs args)
    {
        ApplyUiSnapshot(args.Snapshot);
    }

    private static bool HasUiContent(PluginUiSnapshot snapshot)
    {
        return snapshot.Values is { Count: > 0 }
            || snapshot.Actions is { Count: > 0 }
            || snapshot.Elements?.Any(element => element is not null
                && !string.IsNullOrWhiteSpace(element.Id)
                && element.Kind != PluginUiElementKind.Unknown) == true
            || snapshot.InputSurface is not null
            || snapshot.Status is not null
            || snapshot.Dialog is not null
            || !string.IsNullOrWhiteSpace(snapshot.StatusMessage);
    }

    private void RaisePendingUiDialog()
    {
        if (_uiSnapshot?.Dialog is not { } dialog
            || string.IsNullOrWhiteSpace(dialog.Id)
            || UiDialogRequested is null
            || string.Equals(_lastShownDialogId, dialog.Id, StringComparison.Ordinal))
        {
            return;
        }

        _lastShownDialogId = dialog.Id;
        UiDialogRequested.Invoke(this, new PluginUiDialogRequestedEventArgs(dialog));
    }

    internal void RefreshPluginUiPresentation()
    {
        foreach (var element in _uiElements)
        {
            element.RefreshPresentation();
        }

        OnPropertyChanged(nameof(PluginUiStatusKindLabel));
    }

    internal Task ExecuteUiElementAsync(
        PluginUiElementViewModel element,
        string? argument)
    {
        ArgumentNullException.ThrowIfNull(element);
        return ExecuteUiElementCoreAsync(element.ActionId, argument, element.IsEnabled);
    }

    internal Task ExecuteUiActionAsync(string actionId, string? argument)
    {
        return ExecuteUiElementCoreAsync(actionId, argument, descriptorEnabled: true);
    }

    internal Task ExecuteUiMenuItemAsync(PluginUiMenuItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ExecuteUiElementCoreAsync(item.ActionId, item.Argument, item.IsEnabled);
    }

    private Task ExecuteUiElementCoreAsync(
        string? actionId,
        string? argument,
        bool descriptorEnabled)
    {
        return string.IsNullOrWhiteSpace(actionId) || !descriptorEnabled
            ? Task.CompletedTask
            : ExecuteUiActionCoreAsync(actionId, argument);
    }

    internal void RaisePendingUiDialogForHost()
    {
        RaisePendingUiDialog();
    }
}
