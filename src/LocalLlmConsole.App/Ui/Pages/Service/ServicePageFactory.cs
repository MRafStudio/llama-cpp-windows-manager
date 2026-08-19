namespace LocalLlmConsole.Ui.Pages.Service;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalLlmConsole.Localization;

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
    /// <summary>
    /// Создаёт ComboBox с ItemTemplate, привязанным к указанному свойству.
    /// ItemTemplate применяется и к выбранному элементу в свёрнутом поле
    /// (в отличие от DisplayMemberPath, который работает только в списке).
    /// </summary>
    private static ComboBox CreateServiceCombo(string displayPath)
    {
        var template = new System.Windows.DataTemplate();
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(displayPath));
        template.VisualTree = factory;

        return new ComboBox
        {
            MinWidth = 340,
            MaxWidth = 480,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemTemplate = template
        };
    }

    public static ServicePageBuildResult Create(ServicePageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.ViewModel);

        var root = new DockPanel { Margin = new Thickness(16) };

        // Заголовок
        var headerText = new TextBlock
        {
            Text = Loc.T("Service.PageTitle"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16)
        };
        root.Children.Add(headerText);

        // Статус службы
        var statusLabel = new TextBlock
        {
            Text = Loc.T("Service.StatusLabel"),
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.Children.Add(statusLabel);

        var statusText = new TextBlock
        {
            Name = "ServiceStatusText",
            Text = Loc.T("Service.Status.Loading"),
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

        // --- Выбор параметров запуска службы: модель / профиль / среда ---
        var selectionPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        var selectionLabel = new TextBlock
        {
            Text = Loc.T("Service.SelectionLabel"),
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 4)
        };
        selectionPanel.Children.Add(selectionLabel);

        // Модель
        selectionPanel.Children.Add(new TextBlock
        {
            Text = Loc.T("Service.ModelLabel"),
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 2)
        });
        var modelCombo = CreateServiceCombo("Name");
        modelCombo.SetBinding(ComboBox.ItemsSourceProperty, new System.Windows.Data.Binding("Models")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        modelCombo.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding("SelectedModel")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay
        });
        selectionPanel.Children.Add(modelCombo);

        // Профиль
        selectionPanel.Children.Add(new TextBlock
        {
            Text = Loc.T("Service.ProfileLabel"),
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 2)
        });
        var profileCombo = CreateServiceCombo("Name");
        profileCombo.SetBinding(ComboBox.ItemsSourceProperty, new System.Windows.Data.Binding("Profiles")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        profileCombo.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding("SelectedProfile")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay
        });
        selectionPanel.Children.Add(profileCombo);

        // Среда выполнения
        selectionPanel.Children.Add(new TextBlock
        {
            Text = Loc.T("Service.RuntimeLabel"),
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 2)
        });
        var runtimeCombo = CreateServiceCombo("Name");
        runtimeCombo.SetBinding(ComboBox.ItemsSourceProperty, new System.Windows.Data.Binding("Runtimes")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        runtimeCombo.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding("SelectedRuntime")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay
        });
        selectionPanel.Children.Add(runtimeCombo);

        root.Children.Add(selectionPanel);

        // Кнопки управления
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };

        var startButton = new Button
        {
            Content = Loc.T("Service.StartButton"),
            Width = 160,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = true
        };
        VisualRole.SetButtonRole(startButton, VisualRole.Primary);
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
            Content = Loc.T("Service.StopButton"),
            Width = 160,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = true
        };
        VisualRole.SetButtonRole(stopButton, VisualRole.Danger);
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
            Content = Loc.T("Service.RestartButton"),
            Width = 160,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = true
        };
        VisualRole.SetButtonRole(restartButton, VisualRole.Quiet);
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

        // Раздел установки / удаления службы
        var installHeader = new TextBlock
        {
            Text = Loc.T("Service.InstallSection"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(installHeader);

        var installPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };

        var installButton = new Button
        {
            Content = Loc.T("Service.InstallButton"),
            Width = 160,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = true
        };
        VisualRole.SetButtonRole(installButton, VisualRole.Primary);
        var installCommandBinding = new System.Windows.Data.Binding("InstallCommand")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        installButton.SetBinding(Button.CommandProperty, installCommandBinding);
        var installEnabledBinding = new System.Windows.Data.Binding("CanInstall")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        installButton.SetBinding(Button.IsEnabledProperty, installEnabledBinding);

        var uninstallButton = new Button
        {
            Content = Loc.T("Service.UninstallButton"),
            Width = 160,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = true
        };
        VisualRole.SetButtonRole(uninstallButton, VisualRole.Danger);
        var uninstallCommandBinding = new System.Windows.Data.Binding("UninstallCommand")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        uninstallButton.SetBinding(Button.CommandProperty, uninstallCommandBinding);
        var uninstallEnabledBinding = new System.Windows.Data.Binding("CanUninstall")
        {
            Source = request.ViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        uninstallButton.SetBinding(Button.IsEnabledProperty, uninstallEnabledBinding);

        installPanel.Children.Add(installButton);
        installPanel.Children.Add(uninstallButton);
        root.Children.Add(installPanel);

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
            Text = Loc.T("Service.Description"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.DarkGray),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        root.Children.Add(descriptionText);

        // Ошибки
        var errorLabel = new TextBlock
        {
            Text = Loc.T("Service.MessagesLabel"),
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
            Mode = System.Windows.Data.BindingMode.OneWay,
            Converter = new System.Windows.Controls.BooleanToVisibilityConverter()
        };
        errorText.SetBinding(TextBlock.VisibilityProperty, isErrorBinding);

        root.Children.Add(errorText);

        // Set DockPanel alignment
        DockPanel.SetDock(headerText, Dock.Top);
        DockPanel.SetDock(statusLabel, Dock.Top);
        DockPanel.SetDock(statusText, Dock.Top);
        DockPanel.SetDock(selectionPanel, Dock.Top);
        DockPanel.SetDock(buttonPanel, Dock.Top);
        DockPanel.SetDock(installHeader, Dock.Top);
        DockPanel.SetDock(installPanel, Dock.Top);
        DockPanel.SetDock(separator, Dock.Top);
        DockPanel.SetDock(descriptionText, Dock.Top);
        DockPanel.SetDock(errorLabel, Dock.Top);
        DockPanel.SetDock(errorText, Dock.Top);

        return new ServicePageBuildResult(root, new ServicePageContent());
    }
}