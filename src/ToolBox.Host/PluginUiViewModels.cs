using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ToolBox.PluginSdk;

namespace ToolBox.Host;

internal sealed class PluginUiDialogRequestedEventArgs(PluginUiDialog dialog) : EventArgs
{
    public PluginUiDialog Dialog { get; } = dialog ?? throw new ArgumentNullException(nameof(dialog));
}

public sealed class PluginUiElementTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ValueTemplate { get; set; }

    public DataTemplate? ActionTemplate { get; set; }

    public DataTemplate? MenuTemplate { get; set; }

    public DataTemplate? SelectControlTemplate { get; set; }

    public DataTemplate? MultiSelectTemplate { get; set; }

    public DataTemplate? ToggleTemplate { get; set; }

    public DataTemplate? CheckBoxTemplate { get; set; }

    public DataTemplate? RadioGroupTemplate { get; set; }

    public DataTemplate? TextBoxTemplate { get; set; }

    public DataTemplate? NumberBoxTemplate { get; set; }

    public DataTemplate? SliderTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject? container)
    {
        return item is not PluginUiElementViewModel element
            ? null
            : element.Kind switch
            {
                PluginUiElementKind.Value => ValueTemplate,
                PluginUiElementKind.Action => ActionTemplate,
                PluginUiElementKind.Menu => MenuTemplate,
                PluginUiElementKind.Select => SelectControlTemplate,
                PluginUiElementKind.MultiSelect => MultiSelectTemplate,
                PluginUiElementKind.Toggle => ToggleTemplate,
                PluginUiElementKind.CheckBox => CheckBoxTemplate,
                PluginUiElementKind.RadioGroup => RadioGroupTemplate,
                PluginUiElementKind.TextBox => TextBoxTemplate,
                PluginUiElementKind.NumberBox => NumberBoxTemplate,
                PluginUiElementKind.Slider => SliderTemplate,
                _ => null
            };
    }
}

public sealed class PluginUiElementViewModel : INotifyPropertyChanged
{
    private readonly PluginWorkspaceViewModel _workspace;
    private readonly ReadOnlyObservableCollection<PluginUiOptionViewModel> _options;
    private readonly ReadOnlyObservableCollection<PluginUiMenuItemViewModel> _menuItems;
    private string _value;
    private bool _isApplying;

    internal PluginUiElementViewModel(
        PluginWorkspaceViewModel workspace,
        PluginUiElement descriptor)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _value = descriptor.Value ?? string.Empty;

        var options = (descriptor.Options ?? Array.Empty<PluginUiOption>())
            .Where(option => option is not null
                && !string.IsNullOrWhiteSpace(option.Value)
                && !string.IsNullOrWhiteSpace(option.Label))
            .Select(option => new PluginUiOptionViewModel(this, option))
            .ToList();
        _options = new ReadOnlyObservableCollection<PluginUiOptionViewModel>(
            new ObservableCollection<PluginUiOptionViewModel>(options));

        foreach (var option in _options)
        {
            option.SetSelectedFromHost(
                descriptor.Kind == PluginUiElementKind.MultiSelect
                    ? (descriptor.Values ?? Array.Empty<string>()).Contains(option.Value, StringComparer.Ordinal)
                    : string.Equals(_value, option.Value, StringComparison.Ordinal));
        }

        var menuItems = (descriptor.MenuItems ?? Array.Empty<PluginUiMenuItem>())
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Label))
            .Select(item => new PluginUiMenuItemViewModel(this, item))
            .ToList();
        _menuItems = new ReadOnlyObservableCollection<PluginUiMenuItemViewModel>(
            new ObservableCollection<PluginUiMenuItemViewModel>(menuItems));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal PluginUiElement Descriptor { get; }

    internal PluginWorkspaceViewModel Workspace => _workspace;

    public string Id => Descriptor.Id;

    public PluginUiElementKind Kind => Descriptor.Kind;

    public string DisplayLabel => _workspace.LocalizePluginUiCommand(
        Descriptor.Command,
        Descriptor.CommandTarget,
        Descriptor.Label);

    public string Description => Descriptor.Description ?? string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Descriptor.Description);

    public string Group => Descriptor.Group ?? string.Empty;

    public bool ShowGroupHeader { get; internal set; }

    public string Value
    {
        get => _value;
        set => UpdateValue(value ?? string.Empty, forceSubmit: false);
    }

    public double NumericValue
    {
        get
        {
            return double.TryParse(
                    _value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                ? Clamp(value)
                : Minimum;
        }
        set => UpdateValue(
            Clamp(value).ToString("R", CultureInfo.InvariantCulture),
            forceSubmit: true);
    }

    public bool? IsChecked
    {
        get => bool.TryParse(_value, out var value) ? value : false;
        set
        {
            if (value.HasValue)
            {
                UpdateValue(value.Value ? "true" : "false", forceSubmit: true);
            }
        }
    }

    public double Minimum => IsFinite(Descriptor.Minimum) ? Descriptor.Minimum!.Value : 0;

    public double Maximum => IsFinite(Descriptor.Maximum) && Descriptor.Maximum > Minimum
        ? Descriptor.Maximum!.Value
        : Math.Max(100, Minimum + 1);

    public double Step => IsFinite(Descriptor.Step) && Descriptor.Step > 0
        ? Descriptor.Step!.Value
        : 1;

    public string Unit => Descriptor.Unit ?? string.Empty;

    public string Placeholder => Descriptor.Placeholder ?? string.Empty;

    public string AccessibleName => string.IsNullOrWhiteSpace(Descriptor.AccessibleName)
        ? DisplayLabel
        : Descriptor.AccessibleName!;

    public bool HasIcon => !string.IsNullOrWhiteSpace(DisplayIcon);

    public bool HasVisibleText => Style != PluginUiActionStyle.Icon || !HasIcon;

    public string DisplayIcon => Descriptor.Icon
        ?? PluginUiCommandIcons.Get(Descriptor.Command);

    public PluginUiCommand Command => Descriptor.Command;

    public PluginUiActionStyle Style => Descriptor.Style is PluginUiActionStyle.Unknown
        ? PluginUiActionStyle.Default
        : Descriptor.Style;

    public ReadOnlyObservableCollection<PluginUiOptionViewModel> Options => _options;

    public ReadOnlyObservableCollection<PluginUiMenuItemViewModel> MenuItems => _menuItems;

    public bool IsEnabled => Descriptor.IsEnabled
        && _workspace.IsPluginUiActionEnabled
        && (!IsInteractive || HasAction || Kind == PluginUiElementKind.Menu);

    public bool HasAction => !string.IsNullOrWhiteSpace(Descriptor.ActionId);

    public bool IsInteractive => Kind is PluginUiElementKind.Action
        or PluginUiElementKind.Menu
        or PluginUiElementKind.Select
        or PluginUiElementKind.MultiSelect
        or PluginUiElementKind.Toggle
        or PluginUiElementKind.CheckBox
        or PluginUiElementKind.RadioGroup
        or PluginUiElementKind.TextBox
        or PluginUiElementKind.NumberBox
        or PluginUiElementKind.Slider;

    internal string? ActionId => Descriptor.ActionId;

    internal string? StaticArgument => Descriptor.Argument;

    internal string BuildArgument()
    {
        if (Kind == PluginUiElementKind.MultiSelect)
        {
            return JsonSerializer.Serialize(
                _options.Where(option => option.IsSelected).Select(option => option.Value).ToArray());
        }

        if (Kind == PluginUiElementKind.NumberBox
            && double.TryParse(
                _value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number)
            && double.IsFinite(number))
        {
            return Clamp(number).ToString("R", CultureInfo.InvariantCulture);
        }

        return _value;
    }

    internal void CommitValue()
    {
        if (Kind == PluginUiElementKind.NumberBox && !NormalizeNumberValue())
        {
            return;
        }

        // LostFocus/Enter updates the binding first. Immediate mode already
        // submits from UpdateValue; commit mode submits at this boundary.
        if (EffectiveUpdateMode != PluginUiUpdateMode.Commit)
        {
            return;
        }

        _ = SubmitAsync(BuildArgument());
    }

    internal void RefreshEnabled()
    {
        OnPropertyChanged(nameof(IsEnabled));
        foreach (var option in _options)
        {
            option.RefreshEnabled();
        }

        foreach (var menuItem in _menuItems)
        {
            menuItem.RefreshEnabled();
        }
    }

    internal void RefreshPresentation()
    {
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(AccessibleName));
        OnPropertyChanged(nameof(DisplayIcon));
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(HasVisibleText));
        foreach (var menuItem in _menuItems)
        {
            menuItem.RefreshPresentation();
        }
    }

    internal void OnOptionSelectionChanged(PluginUiOptionViewModel option, bool selected)
    {
        if (_isApplying || !selected)
        {
            return;
        }

        if (Kind == PluginUiElementKind.RadioGroup)
        {
            _isApplying = true;
            try
            {
                foreach (var current in _options)
                {
                    current.SetSelectedFromHost(ReferenceEquals(current, option));
                }

                UpdateValue(option.Value, forceSubmit: true);
            }
            finally
            {
                _isApplying = false;
            }
        }
        else if (Kind == PluginUiElementKind.MultiSelect)
        {
            OnPropertyChanged(nameof(Value));
            _ = SubmitAsync(BuildArgument());
        }
    }

    private void UpdateValue(string value, bool forceSubmit)
    {
        if (string.Equals(_value, value, StringComparison.Ordinal))
        {
            if (forceSubmit && !_isApplying)
            {
                _ = SubmitAsync(BuildArgument());
            }

            return;
        }

        _value = value;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(NumericValue));
        OnPropertyChanged(nameof(IsChecked));

        if (!_isApplying && (forceSubmit || EffectiveUpdateMode == PluginUiUpdateMode.Immediate))
        {
            if (Kind == PluginUiElementKind.NumberBox && !NormalizeNumberValue())
            {
                return;
            }

            _ = SubmitAsync(BuildArgument());
        }
    }

    private bool NormalizeNumberValue()
    {
        if (!double.TryParse(
                _value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number)
            || !double.IsFinite(number))
        {
            return false;
        }

        var normalized = Clamp(number).ToString("R", CultureInfo.InvariantCulture);
        if (string.Equals(_value, normalized, StringComparison.Ordinal))
        {
            return true;
        }

        _value = normalized;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(NumericValue));
        return true;
    }

    private PluginUiUpdateMode EffectiveUpdateMode => Descriptor.UpdateMode switch
    {
        PluginUiUpdateMode.Immediate => PluginUiUpdateMode.Immediate,
        PluginUiUpdateMode.Commit => PluginUiUpdateMode.Commit,
        _ when Kind is PluginUiElementKind.TextBox or PluginUiElementKind.NumberBox
            => PluginUiUpdateMode.Commit,
        _ => PluginUiUpdateMode.Immediate
    };

    private async Task SubmitAsync(string argument)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(ActionId))
        {
            return;
        }

        var submittedArgument = Kind is PluginUiElementKind.Action or PluginUiElementKind.Menu
            ? StaticArgument
            : argument;
        await _workspace.ExecuteUiElementAsync(this, submittedArgument).ConfigureAwait(false);
    }

    private double Clamp(double value)
    {
        if (!double.IsFinite(value))
        {
            return Minimum;
        }

        return Math.Clamp(value, Minimum, Maximum);
    }

    private static bool IsFinite(double? value)
    {
        return value.HasValue && double.IsFinite(value.Value);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class PluginUiOptionViewModel : INotifyPropertyChanged
{
    private readonly PluginUiElementViewModel _element;
    private bool _isSelected;

    internal PluginUiOptionViewModel(
        PluginUiElementViewModel element,
        PluginUiOption descriptor)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal PluginUiOption Descriptor { get; }

    public string Value => Descriptor.Value;

    public string Label => Descriptor.Label;

    public string Description => Descriptor.Description ?? string.Empty;

    public bool IsEnabled => Descriptor.IsEnabled && _element.IsEnabled;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            _element.OnOptionSelectionChanged(this, value);
        }
    }

    public string GroupName => $"plugin-{_element.Workspace.PluginId}-{_element.Id}";

    internal void SetSelectedFromHost(bool selected)
    {
        if (_isSelected == selected)
        {
            return;
        }

        _isSelected = selected;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
    }

    internal void RefreshEnabled()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
    }
}

public sealed class PluginUiMenuItemViewModel : INotifyPropertyChanged
{
    private readonly PluginUiElementViewModel _element;
    private readonly ReadOnlyObservableCollection<PluginUiMenuItemViewModel> _children;

    internal PluginUiMenuItemViewModel(
        PluginUiElementViewModel element,
        PluginUiMenuItem descriptor)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _children = new ReadOnlyObservableCollection<PluginUiMenuItemViewModel>(
            new ObservableCollection<PluginUiMenuItemViewModel>(
                (descriptor.Children ?? Array.Empty<PluginUiMenuItem>())
                    .Where(child => child is not null && !string.IsNullOrWhiteSpace(child.Label))
                    .Select(child => new PluginUiMenuItemViewModel(element, child))));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal PluginUiMenuItem Descriptor { get; }

    public string DisplayLabel => _element.Workspace.LocalizePluginUiCommand(
        Descriptor.Command,
        null,
        Descriptor.Label);

    public bool HasIcon => !string.IsNullOrWhiteSpace(DisplayIcon);

    public string DisplayIcon => Descriptor.Icon
        ?? PluginUiCommandIcons.Get(Descriptor.Command);

    public bool IsEnabled => Descriptor.IsEnabled && _element.IsEnabled;

    public bool IsChecked => Descriptor.IsChecked;

    public ReadOnlyObservableCollection<PluginUiMenuItemViewModel> Children => _children;

    public bool HasChildren => _children.Count > 0;

    internal string? ActionId => Descriptor.ActionId;

    internal string? Argument => Descriptor.Argument;

    internal PluginWorkspaceViewModel Workspace => _element.Workspace;

    internal void RefreshEnabled()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        foreach (var child in _children)
        {
            child.RefreshEnabled();
        }
    }

    internal void RefreshPresentation()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayIcon)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIcon)));
        foreach (var child in _children)
        {
            child.RefreshPresentation();
        }
    }
}

internal static class PluginUiCommandIcons
{
    public static string Get(PluginUiCommand command)
    {
        return command switch
        {
            PluginUiCommand.Refresh => "↻",
            PluginUiCommand.Retry => "↶",
            PluginUiCommand.Search => "⌕",
            PluginUiCommand.Scan => "⌁",
            PluginUiCommand.Start => "▶",
            PluginUiCommand.Stop => "■",
            PluginUiCommand.Pause => "Ⅱ",
            PluginUiCommand.Resume => "▶",
            PluginUiCommand.Cancel => "×",
            PluginUiCommand.Connect => "⇄",
            PluginUiCommand.Disconnect => "⨯",
            PluginUiCommand.Reconnect => "↻",
            PluginUiCommand.Save => "▣",
            PluginUiCommand.Apply => "✓",
            PluginUiCommand.Reset => "↺",
            PluginUiCommand.Add => "+",
            PluginUiCommand.Delete => "−",
            PluginUiCommand.Copy => "⧉",
            PluginUiCommand.Import => "↓",
            PluginUiCommand.Export => "↑",
            PluginUiCommand.Open => "↗",
            PluginUiCommand.Play => "▶",
            PluginUiCommand.Previous => "◀",
            PluginUiCommand.Next => "▶",
            PluginUiCommand.Rewind => "«",
            PluginUiCommand.FastForward => "»",
            PluginUiCommand.Mute => "🔇",
            PluginUiCommand.Unmute => "🔊",
            PluginUiCommand.VolumeUp => "🔊",
            PluginUiCommand.VolumeDown => "🔉",
            PluginUiCommand.Close => "×",
            PluginUiCommand.Settings => "⚙",
            PluginUiCommand.Help => "?",
            PluginUiCommand.More => "⋯",
            _ => string.Empty
        };
    }
}
