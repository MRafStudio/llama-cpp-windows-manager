using System.Windows;
using System.Windows.Controls;

namespace LocalLlmConsole;

/// <summary>Состояние прогресса загрузки обновления.</summary>
public sealed record UpdateProgressState(double Percent, string Text);

/// <summary>
/// Модальное окно прогресса загрузки обновления (собственное).
/// Показывается в основном приложении — никаких консольных окон.
/// </summary>
public sealed class UpdateProgressWindow : Window
{
    private readonly System.Windows.Controls.ProgressBar _bar;
    private readonly TextBlock _status;

    public UpdateProgressWindow(string title)
    {
        Title = title;
        Width = 440;
        Height = 140;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        ShowInTaskbar = false;
        Owner = System.Windows.Application.Current?.MainWindow;
        Topmost = true;

        var panel = new StackPanel { Margin = new Thickness(16) };
        _status = new TextBlock
        {
            Text = "Готовим обновление...",
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        _bar = new System.Windows.Controls.ProgressBar { Height = 20, Minimum = 0, Maximum = 100 };
        panel.Children.Add(_status);
        panel.Children.Add(_bar);
        Content = panel;
    }

    public void SetState(UpdateProgressState state) => Dispatcher.Invoke(() =>
    {
        _status.Text = state.Text;
        if (state.Percent < 0)
            _bar.IsIndeterminate = true;
        else
        {
            _bar.IsIndeterminate = false;
            _bar.Value = state.Percent;
        }
    });
}
