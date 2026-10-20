using CommunityToolkit.Mvvm.ComponentModel;

namespace Sfp.App.ViewModels.Generate;

/// <summary>One preview slot: a caption, then either the rendered PNG, a placeholder line or an error line.</summary>
public sealed partial class PreviewThumbnail : ObservableObject
{
    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private byte[]? _png;

    [ObservableProperty]
    private int _widthPx;

    [ObservableProperty]
    private int _heightPx;

    /// <summary>The placeholder or error line shown instead of an image.</summary>
    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isRendering;

    public PreviewThumbnail(string caption)
    {
        Caption = caption;
    }

    /// <summary>What the slot shows: "First daily page", "First calendar month", "Cover".</summary>
    public string Caption { get; }

    public bool HasImage => Png is not null;

    /// <summary>Drops the image and shows a placeholder line.</summary>
    public void ShowPlaceholder(string text)
    {
        IsRendering = false;
        Png = null;
        WidthPx = 0;
        HeightPx = 0;
        Title = "";
        Status = text;
    }

    /// <summary>Marks the slot as rendering, keeping the previous image until the new one arrives.</summary>
    public void BeginRendering()
    {
        IsRendering = true;
        Status = Png is null ? "Rendering..." : null;
    }

    public void ShowImage(string title, byte[] png, int widthPx, int heightPx)
    {
        Title = title;
        Png = png;
        WidthPx = widthPx;
        HeightPx = heightPx;
        Status = null;
        IsRendering = false;
    }

    public void ShowError(string message)
    {
        IsRendering = false;
        Png = null;
        WidthPx = 0;
        HeightPx = 0;
        Status = message;
    }
}
