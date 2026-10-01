using System.Windows;
using Kaydence.Models;

namespace Kaydence.Controls;

// I'm what every thing on my page shares, so saving, undo and deleting treat them all the same
public interface IPageElement
{
    event Action? Changed;
    event Action<UIElement>? RemoveRequested;
    event Action<UIElement, bool>? OrderRequested;

    bool IsBlank { get; }
    string PlainText { get; }
    PageItem ToItem(double x, double y, int z);
}
