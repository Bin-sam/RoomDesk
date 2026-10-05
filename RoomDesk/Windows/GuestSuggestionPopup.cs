using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RoomDesk.Core;

namespace RoomDesk.Windows;

internal sealed class GuestSuggestionPopup
{
    private readonly Popup popup;
    private readonly ListBox list;
    private readonly TextBox input;
    private readonly BoardStore store;
    private readonly string field;
    private readonly Action<GuestSuggestion> select;
    private int sequence;
    private bool closed;
    public GuestSuggestionPopup(Window owner, TextBox input, BoardStore store, string field, Action<GuestSuggestion> select)
    {
        this.input = input; this.store = store; this.field = field; this.select = select;
        list = new ListBox { MaxHeight = 190, DisplayMemberPath = "Display", Background = Brushes.White };
        popup = new Popup { PlacementTarget = input, Placement = PlacementMode.Bottom, StaysOpen = true,
            Child = new Border { BorderBrush = Brushes.DarkSeaGreen, BorderThickness = new Thickness(1), Child = list } };
        input.TextChanged += async (_, _) => await Lookup();
        input.GotKeyboardFocus += async (_, _) => await Lookup();
        input.LostKeyboardFocus += (_, _) => { if (!list.IsKeyboardFocusWithin) Reset(); };
        input.PreviewKeyDown += (_, e) =>
        {
            if (!popup.IsOpen) return;
            if (e.Key == Key.Escape) { e.Handled = true; Reset(); }
            else if (e.Key is Key.Down or Key.Up && list.Items.Count > 0)
            {
                e.Handled = true;
                list.SelectedIndex = list.SelectedIndex < 0 ? (e.Key == Key.Down ? 0 : list.Items.Count - 1)
                    : (list.SelectedIndex + (e.Key == Key.Down ? 1 : -1) + list.Items.Count) % list.Items.Count;
                list.ScrollIntoView(list.SelectedItem);
            }
            else if (e.Key == Key.Enter) { e.Handled = true; Choose(); }
        };
        // Keep input focus until selection is committed; otherwise focus loss closes the popup first.
        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem item
                && item.DataContext is GuestSuggestion guest) { e.Handled = true; Reset(); select(guest); }
        };
        owner.Deactivated += (_, _) => Reset();
        owner.Closed += (_, _) => { closed = true; Reset(); };
    }
    public void Reset() { sequence++; popup.IsOpen = false; list.ItemsSource = null; }
    private void Choose() { if (list.SelectedItem is GuestSuggestion guest) { Reset(); select(guest); } }
    private async Task Lookup()
    {
        Reset(); if (closed || !input.IsKeyboardFocused || !input.IsEnabled) return;
        var query = input.Text.Trim(); if (query.Length == 0) return;
        var current = sequence;
        await Task.Delay(200);
        if (current != sequence || closed) return;
        try
        {
            var matches = await Task.Run(() => store.SuggestGuestsAsync(query, field));
            if (current != sequence || closed || !input.IsKeyboardFocused) return;
            list.ItemsSource = matches; list.SelectedIndex = -1;
            list.Width = Math.Max(340, input.ActualWidth); popup.IsOpen = matches.Count > 0;
        }
        catch { if (current == sequence) Reset(); } // Manual input remains available if history lookup fails.
    }
}
