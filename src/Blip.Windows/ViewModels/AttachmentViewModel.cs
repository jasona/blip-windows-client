using Avalonia.Media.Imaging;
using Blip.Core;
using Blip.Core.Cache;
using Blip.Core.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Blip.Windows.ViewModels;

public sealed partial class AttachmentViewModel : ObservableObject
{
    public const int PreviewDim = 800;
    /// <summary>Long edge the in-app viewer asks for: sharp on a 4K screen, a fraction of a full HEIC.</summary>
    public const int ViewerDim = 2560;
    private Task? _full;
    private readonly AttachmentMeta _meta;
    private readonly BlipClient _client;
    private readonly AttachmentCache _cache;

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string _status = "";
    /// <summary>The viewer's copy; until it arrives the viewer shows the inline preview.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Shown))] private Bitmap? _fullImage;
    [ObservableProperty] private bool _loadingFull;

    public Bitmap? Shown => FullImage ?? Image;
    partial void OnImageChanged(Bitmap? value) => OnPropertyChanged(nameof(Shown));

    public AttachmentViewModel(AttachmentMeta meta, BlipClient client, AttachmentCache cache)
    {
        _meta = meta; _client = client; _cache = cache;
        if (IsImage) _ = LoadPreviewAsync();
    }

    public string Name => _meta.Name;
    public string Detail => Formatting.Size(_meta.Bytes);
    public bool IsImage => _meta.IsImage && _meta.Id is not null && _meta.Sticker is null;
    public bool IsChip => !IsImage;
    public string Glyph => _meta.IsAudio ? "🎵" : _meta.IsImage ? "🖼️" : "📄";

    private async Task LoadPreviewAsync()
    {
        try
        {
            var path = await _cache.GetAsync(_meta.Id!, _meta.Name, PreviewDim,
                async (s, ct) => (await _client.FetchAttachmentAsync(_meta.Id!, s, PreviewDim, ct: ct)).Ok);
            if (path is null) { await Services.Ui.InvokeAsync(Unavailable); return; }
            // Decode from memory so the cache file is not held open.
            var bytes = await File.ReadAllBytesAsync(path);
            var bmp = await Task.Run(() => Services.ImageDecode.ToWidth(bytes, 640));
            await Services.Ui.InvokeAsync(() => Image = bmp);
        }
        catch
        {
            // Not an image we can draw (or the Mac could not convert it): say so, and let a click
            // hand the file to its default app instead of opening an empty viewer.
            await Services.Ui.InvokeAsync(Unavailable);
        }
    }

    /// <summary>No inline preview: a click opens the file in its default app instead of the viewer.</summary>
    public bool PreviewFailed { get; private set; }

    private void Unavailable()
    {
        PreviewFailed = true;
        Status = "preview unavailable";
    }

    /// <summary>A large copy for the in-app viewer. Once per attachment; failures leave the preview showing.</summary>
    public Task LoadFullAsync() => _full ??= LoadFullCoreAsync();

    private async Task LoadFullCoreAsync()
    {
        if (!IsImage) return;
        LoadingFull = true;
        try
        {
            var path = await _cache.GetAsync(_meta.Id!, _meta.Name, ViewerDim,
                async (s, ct) => (await _client.FetchAttachmentAsync(_meta.Id!, s, ViewerDim, ct: ct)).Ok);
            if (path is null) { _full = null; return; }
            var bytes = await File.ReadAllBytesAsync(path);
            var bmp = await Task.Run(() => Services.ImageDecode.ToWidth(bytes, ViewerDim));
            await Services.Ui.InvokeAsync(() => FullImage = bmp);
        }
        catch { _full = null; /* the preview stays; a later open tries again */ }
        finally { await Services.Ui.InvokeAsync(() => LoadingFull = false); }
    }

    /// <summary>The name a saved copy gets: HEIC comes over as JPEG.</summary>
    public string SaveName => IsHeic ? Path.ChangeExtension(_meta.Name, ".jpg") : _meta.Name;
    private bool IsHeic => _meta.Mime?.Contains("hei", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The full file in the attachment cache, fetched once. HEIC comes over as JPEG.</summary>
    public async Task<string?> OriginalAsync()
    {
        if (_meta.Id is null) return null;
        var heic = IsHeic;
        return await _cache.GetAsync(_meta.Id, SaveName, 0,
            async (s, ct) => (await _client.FetchAttachmentAsync(_meta.Id, s, 0, convertJpeg: heic, ct: ct)).Ok);
    }

    /// <summary>Fetch the full file and open it with its default app.</summary>
    [RelayCommand]
    private async Task OpenAsync()
    {
        if (_meta.Id is null) return;
        Status = "Downloading…";
        try
        {
            var path = await OriginalAsync();
            if (path is null) { Status = "could not download"; return; }
            Status = Services.SafeOpen.Open(path) ?? "";
        }
        catch (Exception e)
        {
            Status = e.Message;
        }
    }
}
