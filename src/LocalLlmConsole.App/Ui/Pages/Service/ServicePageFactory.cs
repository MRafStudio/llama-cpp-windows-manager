namespace LocalLlmConsole.Ui.Pages.Service;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

/// <summary>
/// Страница управления службой llama-server.
/// </summary>
public sealed class ServicePageContent
{
    public Button StartServiceButton { get; private set; } = null!;
    public Button StopServiceButton { get; private set; } = null!;
    public Button RestartServiceButton { get; private set; } = null!;
    public TextBlock ServiceStatusText { get; private set; } = null!;
    public TextBlock ErrorMessageText { get; private set; } = null!;
}

public sealed record ServicePageRequest(
    LocalLlmConsole.ViewModels.LlamaServiceViewModel ViewModel);

public sealed record ServicePageBuildResult(
    DockPanel Content,
    ServicePageContent Controls);

public static class ServicePageFactory
{
    public static ServicePageBuildResult Create(ServicePageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.ViewModel);

        var root = new DockPanel { Margin = new Thickness(16) };

        // Заголовок
        var headerText = new TextBlock
        {
            Text = "Управление службой llama-server",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16)
        };
        root.Children.Add(headerText);

        // Статус службы
        var statusLabel = new TextBlock
        {
            Text = "Статус службы:",
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.Children.Add(statusLabel);

        var statusText = new TextBlock
        {
            Name = "ServiceStatusText",
            Text = "Загрузка...",
            FontSize = 14,
            Foreground = new SolidColorBrush(Colors.Gray),
            Margin = new Thickness(0, 0, 0, 16)
        };

        // Привязка к ViewModel
        var statusBinding = new System.Windows.Data.Binding("ServiceStatus")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        statusText.SetBinding(TextBlock.TextProperty, statusBinding);

        // Цвет статуса
        var colorBinding = new System.Windows.Data.Binding("ServiceStatusColor")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        var colorConverter = new System.Windows.Media.BrushConverter();
        statusText.SetBinding(TextBlock.ForegroundProperty, colorBinding);

        root.Children.Add(statusText);

        // Кнопки управления
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };

        var startButton = new Button
        {
            Content = "Запустить службу",
            Style = (Style)Application.Current.FindResource("PrimaryButton"),
            Width = 160,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = true
        };
        var startCommandBinding = new System.Windows.Data.Binding("StartCommand")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        startButton.SetBinding(Button.CommandProperty, startCommandBinding);
        var startEnabledBinding = new System.Windows.Data.Binding("CanStart")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        startButton.SetBinding(Button.IsEnabledProperty, startEnabledBinding);

        var stopButton = new Button
        {
            Content = "Остановить службу",
            Style = (Style)Application.Current.FindResource("DangerButton"),
            Width = 160,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = true
        };
        var stopCommandBinding = new System.Windows.Data.Binding("StopCommand")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        stopButton.SetBinding(Button.CommandProperty, stopCommandBinding);
        var stopEnabledBinding = new System.Windows.Data.Binding("CanStop")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        stopButton.SetBinding(Button.IsEnabledProperty, stopEnabledBinding);

        var restartButton = new Button
        {
            Content = "Перезапустить",
            Style = (Style)Application.Current.FindResource("SecondaryButton"),
            Width = 160,
            IsEnabled = true
        };
        var restartCommandBinding = new System.Windows.Data.Binding("RestartCommand")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        restartButton.SetBinding(Button.CommandProperty, restartCommandBinding);
        var restartEnabledBinding = new System.Windows.Data.Binding("CanRestart")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        restartButton.SetBinding(Button.IsEnabledProperty, restartEnabledBinding);

        buttonPanel.Children.Add(startButton);
        buttonPanel.Children.Add(stopButton);
        buttonPanel.Children.Add(restartButton);
        root.Children.Add(buttonPanel);

        // Разделитель
        var separator = new Separator
        {
            Margin = new Thickness(0, 0, 0, 16),
            BorderBrush = new SolidColorBrush(Colors.LightGray),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        root.Children.Add(separator);

        // Описание
        var descriptionText = new TextBlock
        {
            Text = "Служба позволяет запускать llama-server в фоновом режиме как Windows-службу. " +
                   "Это обеспечивает автоматический перезапуск при сбоях и независимость от GUI приложения.\n\n" +
                   "Изменения параметров требуют ручного перезапуска службы через кнопку 'Перезапустить'.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.DarkGray),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        root.Children.Add(descriptionText);

        // Ошибки
        var errorLabel = new TextBlock
        {
            Text = "Сообщения:",
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.Children.Add(errorLabel);

        var errorText = new TextBlock
        {
            Name = "ErrorMessageText",
            Text = "",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.Red),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 0)
        };
        var errorBinding = new System.Windows.Data.Binding("LastMessage")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        errorText.SetBinding(TextBlock.TextProperty, errorBinding);
        var isErrorBinding = new System.Windows.Data.Binding("HasError")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        errorText.SetBinding(TextBlock.VisibilityProperty, isErrorBinding);

        root.Children.Add(errorText);

        // Set DockPanel alignment
        DockPanel.SetDock(headerText, Dock.Top);
        DockPanel.SetDock(statusLabel, Dock.Top);
        DockPanel.SetDock(statusText, Dock.Top);
        DockPanel.SetDock(buttonPanel, Dock.Top);
        DockPanel.SetDock(separator, Dock.Top);
        DockPanel.SetDock(descriptionText, Dock.Top);
        DockPanel.SetDock(errorLabel, Dock.Top);
        DockPanel.SetDock(errorText, Dock.Top);

        return new ServicePageBuildResult(root, new ServicePageContent());
    }
}