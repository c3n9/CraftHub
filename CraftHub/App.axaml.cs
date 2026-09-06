using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CraftHub.Services;
using CraftHub.Services.ServicesCollectionExtension;
using CraftHub.ViewModels;
using CraftHub.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;

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


            if (desktop.Args is { Length: > 0 })
            {
                var paths = desktop.Args.Where(File.Exists).ToList();
                if (paths.Count > 0)
                    _ = mainVm.OpenDroppedFilesAsync(paths);
            }

            SubscribeToFileActivation(mainVm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Opens files the OS hands us after startup. Command-line arguments cover Windows and
    /// Linux, where the shell launches the executable with the path — but macOS does not pass a
    /// double-clicked document in argv at all: it sends the running application an open-documents
    /// Apple Event, which Avalonia surfaces here. Without this, associating a file type on macOS
    /// would open an empty window, which is worse than no association.
    ///
    /// It also fires while the app is already running (a second double-click), so files arriving
    /// this way are opened into the existing window rather than a new process.</summary>
    private void SubscribeToFileActivation(MainWindowViewModel mainVm)
    {
        if (TryGetFeature(typeof(IActivatableLifetime)) is not IActivatableLifetime activatable) return;

        activatable.Activated += (_, e) =>
        {
            if (e is not FileActivatedEventArgs fileArgs) return;

            // TryGetLocalPath returns null for anything not backed by a real file (an iCloud
            // placeholder, a security-scoped URL we cannot resolve) — those are skipped rather
            // than reported, exactly as an unreadable command-line argument already is.
            var paths = fileArgs.Files
                .Select(f => f.TryGetLocalPath())
                .Where(path => path is not null && File.Exists(path))
                .Select(path => path!)
                .ToList();

            if (paths.Count > 0)
                _ = mainVm.OpenDroppedFilesAsync(paths);
        };
    }
}

