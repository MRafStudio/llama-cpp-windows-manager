using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LocalLlmConsole;

/// <summary>Состояние прогресса загрузки обновления.</summary>
public sealed record UpdateProgressState(double Percent, string Text);

/// <summary>
/// Модальное окно прогресса загрузки обновления (собственное), в тёмной теме
/// приложения (как ThemedMessageBox). Показывается в основном приложении —
/// никаких консольных окон.
/// </summary>
public sealed class UpdateProgressWindow : Window
{
    private readonly System.Windows.Controls.ProgressBar _bar;
    private readonly TextBlock _status;

    private static System.Windows.Media.Brush Brush(string key)
        => (System.Windows.Media.Brush)(System.Windows.Application.Current.TryFindResource(key)
                                        ?? System.Windows.Media.Brushes.White);

    public UpdateProgressWindow(string title)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Owner = System.Windows.Application.Current?.MainWindow;
        Topmost = true;
        Background = System.Windows.Media.Brushes.Transparent;
        AllowsTransparency = true;

        var root = new Border
        {
            Background = Brush("PanelBack"),
            BorderBrush = Brush("PanelBorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 0,
                Opacity = 0.38,
                Color = System.Windows.Media.Color.FromRgb(0, 0, 0)
            }
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        layout.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextMain"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        _status = new TextBlock
        {
            Text = "…",
            FontSize = 13,
            Foreground = Brush("TextSoft"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _bar = new System.Windows.Controls.ProgressBar { Height = 20, Minimum = 0, Maximum = 100 };
        body.Children.Add(_status);
        body.Children.Add(_bar);

        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        root.Child = layout;
        Content = root;
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
