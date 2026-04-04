using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CraftHub.Services;
using CraftHub.ViewModels;
using CraftHub.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CraftHub;

public class App : Application
{
    public static ServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // Register services
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IJsonService, JsonService>();
        services.AddSingleton<IClassParserService, ClassParserService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ThemeService>();
        
        // Register ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<WorkspaceViewModel>();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}


