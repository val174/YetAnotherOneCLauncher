using System.Collections.ObjectModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>
/// Панель команд в узком (сверху) или низком (слева) окне, задача #31: видны только кнопки, которые помещаются
/// целиком; остальные — в меню «…» после последней видимой. Сверху поиск сначала сжимается до наименьшей ширины.
/// </summary>
public partial class MainWindow
{
    /// <summary>С какого элемента панели (по порядку в ToolbarButtons) команды ушли в «…»; нет — -1.</summary>
    private int _toolbarOverflowStart = -1;

    /// <summary>Пункты меню «…» (меню смотрит на эту коллекцию, при открытии она заполняется заново).</summary>
    private readonly ObservableCollection<Control> _toolbarOverflowItems = [];

    private void InitToolbarOverflow()
    {
        SideToolbarHost.SizeChanged += (_, _) => UpdateToolbarOverflow();
        HeaderPanel.SizeChanged += (_, _) => UpdateToolbarOverflow();
        // Кнопки появляются и прячутся (метки, проекты EDT), меняется стиль значков — пересчитать после раскладки.
        ToolbarButtons.LayoutUpdated += (_, _) => UpdateToolbarOverflow();
        var menu = (MenuFlyout)ToolbarOverflowButton.Flyout!;
        menu.ItemsSource = _toolbarOverflowItems;
        menu.Opening += (_, _) => FillToolbarOverflow();
    }

    /// <summary>
    /// Сколько места у панели вдоль неё: слева — высота полосы; сверху — ширина строки за вычетом кнопок справа
    /// и поиска наименьшей ширины с местом для перетаскивания окна. Панель скрыта — 0 (не переполняется).
    /// </summary>
    private double ToolbarAvailableLength(bool left)
    {
        if (left)
        {
            return SideToolbarHost.IsVisible
                ? SideToolbarHost.Bounds.Height - SideToolbarHost.Padding.Top - SideToolbarHost.Padding.Bottom
                : 0;
        }

        var others = HeaderPanel.Children
            .Where(c => c.IsVisible && c != TopToolbarHost && c != SearchHost)
            .Sum(c => c.Bounds.Width + c.Margin.Left + c.Margin.Right);
        return HeaderPanel.Bounds.Width - others - SearchHost.Margin.Left - SearchHost.Margin.Right - MinSearchWidth - MinDragWidth
            - TopToolbarHost.Padding.Left - TopToolbarHost.Padding.Right;
    }

    private void UpdateToolbarOverflow()
    {
        var left = ToolbarFrame.Parent == SideToolbarHost;
        double End(Control c) => left ? c.Bounds.Bottom : c.Bounds.Right;

        var start = -1;
        var visibleLength = double.NaN;
        var available = ToolbarAvailableLength(left);
        var items = ToolbarButtons.Children;
        var end = items.Where(c => c.IsVisible).Select(c => End(c) + (left ? c.Margin.Bottom : c.Margin.Right)).DefaultIfEmpty(0).Max();
        if (available > 0 && end > available)
        {
            // Место под «…»: кнопка и отступ перед ней.
            var limit = available - (left
                ? ToolbarOverflowButton.Height + ToolbarOverflowButton.Margin.Top
                : ToolbarOverflowButton.Width + ToolbarOverflowButton.Margin.Left);
            var last = -1;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].IsVisible && End(items[i]) <= limit)
                {
                    last = i;
                }
            }

            // Черта между группами в конце видимой части не нужна.
            while (last >= 0 && (!items[last].IsVisible || IsSeparator(items[last])))
            {
                last--;
            }

            start = last + 1;
            visibleLength = last >= 0 ? End(items[last]) : 0;
        }

        var (length, other) = left
            ? (ToolbarClip.Height, ToolbarClip.Width)
            : (ToolbarClip.Width, ToolbarClip.Height);
        if (start == _toolbarOverflowStart && length.Equals(visibleLength) && double.IsNaN(other))
        {
            return;
        }

        _toolbarOverflowStart = start;
        ToolbarClip.Height = left ? visibleLength : double.NaN;
        ToolbarClip.Width = left ? double.NaN : visibleLength;
        ToolbarOverflowButton.IsVisible = start >= 0;
    }

    private static bool IsSeparator(Control control) => control.Classes.Contains("toolbarSeparator");

    /// <summary>Меню «…»: команды, которые не поместились, — с теми же значками; черты групп — разделители.</summary>
    private void FillToolbarOverflow()
    {
        var items = _toolbarOverflowItems;
        items.Clear();
        var hidden = _toolbarOverflowStart < 0 ? [] : ToolbarButtons.Children.Skip(_toolbarOverflowStart).Where(c => c.IsVisible);
        foreach (var control in hidden)
        {
            if (IsSeparator(control))
            {
                if (items.Count > 0 && items[^1] is not Separator)
                {
                    items.Add(new Separator());
                }
            }
            else if (control == ListFilterSwitch)
            {
                foreach (var segment in ListFilterSegments.Children.OfType<RadioButton>().Where(r => r.IsVisible))
                {
                    var item = new MenuItem
                    {
                        Name = segment.Name + "Overflow",
                        Header = AutomationProperties.GetName(segment),
                        ToggleType = MenuItemToggleType.Radio,
                        IsChecked = segment.IsChecked == true,
                        Icon = CopyIcon(segment),
                    };
                    item.Click += (_, _) => segment.IsChecked = true;
                    items.Add(item);
                }
            }
            else if (control is Button button)
            {
                items.Add(OverflowItem(button));
            }
        }

        if (items.Count > 0 && items[^1] is Separator)
        {
            items.RemoveAt(items.Count - 1);
        }
    }

    private MenuItem OverflowItem(Button button)
    {
        var item = new MenuItem
        {
            Name = button.Name + "Overflow",
            Header = button == ViewModeButton && DataContext is MainWindowViewModel vm
                ? vm.IsTreeMode ? "Показать плоским списком" : "Показать деревом папок"
                : AutomationProperties.GetName(button),
            Command = button.Command,
            CommandParameter = button.CommandParameter,
            IsEnabled = button.IsEnabled,
            Icon = CopyIcon(button),
        };
        // Переключатель (строка меток): включённый — с галочкой.
        if (button.Classes.Contains("checked"))
        {
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = true;
        }

        // Кнопка с меню («Добавить»): то же меню — подменю пункта.
        if (button.Flyout is MenuFlyout flyout)
        {
            foreach (var source in flyout.Items)
            {
                item.Items.Add(source is MenuItem sub
                    ? new MenuItem { Header = sub.Header, Command = sub.Command, InputGesture = sub.InputGesture }
                    : new Separator());
            }
        }

        return item;
    }

    /// <summary>Значок кнопки для пункта меню: тот же рисунок (у кнопок с несколькими значками — видимый сейчас).</summary>
    private static ToolIcon? CopyIcon(ContentControl source)
    {
        var icon = source.Content switch
        {
            ToolIcon single => single,
            Panel panel => panel.Children.OfType<ToolIcon>().FirstOrDefault(i => i.IsVisible),
            _ => null,
        };
        if (icon is null)
        {
            return null;
        }

        var copy = new ToolIcon
        {
            Data = icon.Data,
            BoldData = icon.BoldData,
            SolidData = icon.SolidData,
            Brush = icon.Brush,
            StrokeThickness = icon.StrokeThickness,
        };
        copy.Classes.Add("menuIcon");
        return copy;
    }
}
