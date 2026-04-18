using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CraftHub.Services;
using CraftHub.Services.ServicesCollectionExtension;
using CraftHub.ViewModels;
using CraftHub.Views;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace CraftHub;

public class App : Application
{
    public IServiceProvider Services { get; private set; }
    public new static App Current => (App)Application.Current;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();

        collection.AddCommonServices();
        collection.AddViewModels();
        collection.AddViews();

        Services = collection.BuildServiceProvider();

        // Initialize localization – must run after Application.Current is fully ready
        LanguageService.Instance.Initialize();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainWindowViewModel>();
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = mainVm;
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}

