using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Встроенный значок средства администрирования: значок из лаунчера.</summary>
/// <param name="Id">Имя в настройках (<see cref="AdminToolIcon.BuiltIn"/>).</param>
/// <param name="Title">Название в меню выбора значка.</param>
public sealed record BuiltInToolIcon(string Id, string Title)
{
    public const string Web = "web";
    public const string App = "app";

    /// <summary>В порядке меню выбора значка.</summary>
    public static IReadOnlyList<BuiltInToolIcon> All { get; } =
    [
        new("pusk", "ПУСК"),
        new("cluster", "Консоль кластера"),
        new("enterprise", "1С: Предприятие"),
        new("designer", "Конфигуратор"),
        new("starter", "Стартер 1С"),
        new("launcher", "Лаунчер"),
        new(Web, "Веб-страница"),
        new(App, "Приложение (App)"),
    ];
}

/// <summary>
/// Значок средства администрирования, как он показывается: картинка или плашка с надписью «App» (значок программы
/// по умолчанию). Подбираемый сам значок сначала показывается значком по умолчанию и заменяется, когда загрузится.
/// </summary>
public sealed partial class AdminToolIconViewModel : ObservableObject
{
    private AdminToolIconViewModel(IImage? image) => Image = image;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppBadge))]
    public partial IImage? Image { get; private set; }

    /// <summary>Нет картинки — плашка «App».</summary>
    public bool IsAppBadge => Image is null;

    /// <summary>Значок инструмента: свой, встроенный или подобранный сам (с загрузкой в фоне).</summary>
    public static AdminToolIconViewModel For(string? icon, string target, IAdminToolIconSource? source)
    {
        if (AdminToolIcon.BuiltInId(icon) is { } id)
        {
            return new AdminToolIconViewModel(BuiltIn(id));
        }

        if (AdminToolIcon.FileName(icon) is { } file && source?.LoadFile(file) is { } custom)
        {
            return new AdminToolIconViewModel(custom);
        }

        var view = new AdminToolIconViewModel(DefaultFor(target));
        if (source is not null && !string.IsNullOrWhiteSpace(target))
        {
            _ = view.LoadAutoAsync(source, target);
        }

        return view;
    }

    /// <summary>Только встроенный значок (для меню выбора).</summary>
    public static AdminToolIconViewModel ForBuiltIn(string id) => new(BuiltIn(id));

    /// <summary>Значок по умолчанию: у веб-сервиса — веб-страница, у программы — плашка «App».</summary>
    public static IImage? DefaultFor(string target) =>
        AdminToolTarget.KindOf(target) == AdminToolKind.WebService ? BuiltIn(BuiltInToolIcon.Web) : null;

    private async Task LoadAutoAsync(IAdminToolIconSource source, string target)
    {
        if (await source.LoadAutoAsync(target) is { } image)
        {
            Image = image;
        }
    }

    /// <summary>Картинка встроенного значка; у «App» её нет — плашка.</summary>
    internal static IImage? BuiltIn(string id) => id switch
    {
        "pusk" => Resource("PuskLogoImage") as IImage,
        "launcher" => LauncherImage,
        "cluster" => Glyph("ClusterIconGeometry", "IconClusterBrush"),
        "enterprise" => Glyph("EnterpriseIconGeometry", "IconViewBrush"),
        "designer" => Glyph("DesignerIconGeometry", "IconCopyBrush"),
        "starter" => Glyph("StarterIconGeometry", "IconSettingsBrush"),
        BuiltInToolIcon.Web => Glyph(WebGeometry, "IconViewBrush"),
        _ => null,
    };

    // Веб-страница: глобус — круг, экватор, меридиан и параллели.
    private static readonly Geometry WebGeometry = Geometry.Parse(
        "M 1.5,8 A 6.5,6.5 0 1 1 14.5,8 A 6.5,6.5 0 1 1 1.5,8 Z M 1.5,8 L 14.5,8 M 8,1.5 A 3.2,6.5 0 0 1 8,14.5 "
        + "A 3.2,6.5 0 0 1 8,1.5 Z M 2.6,4.6 L 13.4,4.6 M 2.6,11.4 L 13.4,11.4");

    private static Bitmap? _launcherImage;

    private static Bitmap LauncherImage =>
        _launcherImage ??= new Bitmap(AssetLoader.Open(new Uri("avares://YetAnotherOneCLauncher/Assets/app.png")));

    private static object? Resource(string key) =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value) ? value : null;

    private static DrawingImage Glyph(string geometryKey, string brushKey) =>
        Glyph(Resource(geometryKey) as Geometry ?? Geometry.Parse("M 2,2 L 14,14"), brushKey);

    // Линии значка 16×16; прозрачный квадрат держит поля, чтобы значок не растягивался до краёв.
    private static DrawingImage Glyph(Geometry geometry, string brushKey) => new()
    {
        Drawing = new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing { Brush = Brushes.Transparent, Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)) },
                new GeometryDrawing
                {
                    Geometry = geometry,
                    Pen = new Pen(Resource(brushKey) as IBrush ?? Brushes.Gray, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
                },
            },
        },
    };
}
