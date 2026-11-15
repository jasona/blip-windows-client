using System.Windows.Media.Imaging;
using Blip.Core;
using Blip.Core.Cache;
using Blip.Core.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Blip.Windows.ViewModels;

public sealed partial class AttachmentViewModel : ObservableObject
{
    public const int PreviewDim = 800;
    private readonly AttachmentMeta _meta;
    private readonly BlipClient _client;
    private readonly AttachmentCache _cache;

    [ObservableProperty] private BitmapImage? _image;
    [ObservableProperty] private string _status = "";

    public AttachmentViewModel(AttachmentMeta meta, BlipClient client, AttachmentCache cache)
    {
        _meta = meta; _client = client; _cache = cache;
        if (IsImage) _ = LoadPreviewAsync();
    }

    public string Name => _meta.Name;
    public string Detail => Formatting.Size(_meta.Bytes);
    public bool IsImage => _meta.IsImage && _meta.Id is not null && _meta.Sticker is null;
    public bool IsChip => !IsImage;
    public string Glyph => _meta.IsAudio ? "" : _meta.IsImage ? "" : "";

    private async Task LoadPreviewAsync()
    {
        try
        {
            var path = await _cache.GetAsync(_meta.Id!, _meta.Name, PreviewDim,
                async (s, ct) => (await _client.FetchAttachmentAsync(_meta.Id!, s, PreviewDim, ct: ct)).Ok);
            if (path is null) { Status = "preview unavailable"; return; }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // do not hold the file open
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 320;
            bmp.EndInit();
            bmp.Freeze();
            Image = bmp;
        }
        catch (Exception e)
        {
            Status = e.Message;
        }
    }

    /// <summary>Fetch the full file and open it with its default app. HEIC comes over as JPEG.</summary>
    [RelayCommand]
    private async Task OpenAsync()
    {
        if (_meta.Id is null) return;
        Status = "Downloading…";
        try
        {
            var heic = _meta.Mime?.Contains("hei", StringComparison.OrdinalIgnoreCase) == true;
            var name = heic ? Path.ChangeExtension(_meta.Name, ".jpg") : _meta.Name;
            var path = await _cache.GetAsync(_meta.Id, name, 0,
                async (s, ct) => (await _client.FetchAttachmentAsync(_meta.Id, s, 0, convertJpeg: heic, ct: ct)).Ok);
            if (path is null) { Status = "could not download"; return; }
            Status = Services.SafeOpen.Open(path) ?? "";
        }
        catch (Exception e)
        {
            Status = e.Message;
        }
    }
}
