using Avalonia;
using Avalonia.Controls;
using Coachlight.Avalonia;
using Coachlight.Avalonia.Building;
using Coachlight.Avalonia.Enums;
using Coachlight.Avalonia.Models;
using Coachlight.Avalonia.Persistence;
using CraftHub.Helpers;
using CraftHub.ViewModels;

namespace CraftHub.Services;

/// <summary>
/// Drives the in-app guided tour (Coachlight). Controls are tagged with
/// <c>Coachmark.Id</c> in the views; this service owns the tour definition and its
/// "show once per user" persistence.
/// </summary>
public interface IOnboardingService
{
    /// <summary>
    /// Show the whole-app tour anchored on <paramref name="anchor"/> (any visual attached to
    /// the main window). When <paramref name="force"/> is <c>false</c> it is shown only the
    /// first time (persisted to the OS app-data folder); <c>true</c> always replays it.
    /// </summary>
    void ShowAppTour(Visual anchor, bool force = false);
}

public sealed class OnboardingService : IOnboardingService
{
    // Bumping this id re-shows the tour to everyone (e.g. after a major UI change).
    private const string AppTourId = "app-overview-v1";

    // JsonProgressStore persists completion to a JSON file under the OS application-data folder.
    private readonly IProgressStore _store = new JsonProgressStore();

    public void ShowAppTour(Visual anchor, bool force = false)
    {
        // The anchor is the main window; its DataContext drives the file-explorer step's
        // reveal/restore. A null vm just means those steps skip the toggle (still shown).
        var vm = (anchor as Control)?.DataContext as MainWindowViewModel;
        anchor.StartTour(BuildAppTour(vm), _store, force);
    }

    // A single long tour over the whole app: welcome → workspace editor → file explorer →
    // window chrome → help. Every target is present on startup (the app always opens with one
    // table-mode workspace), so all coachmarks resolve. Strings are pulled live so the tour
    // follows the current language.
    private static Tour BuildAppTour(MainWindowViewModel? vm)
    {
        // Remembers whether the explorer panel was open before the tour so we can restore it
        // when the user navigates past the explorer steps.
        var explorerWasVisible = false;

        return
        TourBuilder.Create(AppTourId)
            .Labels(new TourLabels
            {
                Skip = Localizer.Get("TourSkip"),
                Back = Localizer.Get("TourBack"),
                Next = Localizer.Get("TourNext"),
                Done = Localizer.Get("TourDone"),
            })
            .Modal(s => s
                .Title(Localizer.Get("TourWelcomeTitle"))
                .Text(Localizer.Get("TourWelcomeText")))

            // ---- Workspace tabs ----
            .Coachmark("addWorkspaceBtn", s => s
                .Placement(Side.Top)
                .Title(Localizer.Get("TourAddWorkspaceTitle"))
                .Text(Localizer.Get("TourAddWorkspaceText")))

            // ---- Workspace editor (WorkspaceView) ----
            .Coachmark("wsSave", s => s
                .Placement(Side.Bottom)
                .Title(Localizer.Get("TourSaveTitle"))
                .Text(Localizer.Get("TourSaveText")))
            .Coachmark(new[] { "importJson", "exportJson" }, s => s
                .Placement(Side.Bottom)
                .Title(Localizer.Get("TourImportExportTitle"))
                .Text(Localizer.Get("TourImportExportText")))
            .Coachmark("wsAddProperty", s => s
                .Placement(Side.Bottom)
                .Title(Localizer.Get("TourAddPropertyTitle"))
                .Text(Localizer.Get("TourAddPropertyText")))
            .Coachmark("wsSchema", s => s
                .Placement(Side.Right)
                .Title(Localizer.Get("TourSchemaTitle"))
                .Text(Localizer.Get("TourSchemaText")))
            .Coachmark("wsGrid", s => s
                .Placement(Side.Left)
                .Title(Localizer.Get("TourGridTitle"))
                .Text(Localizer.Get("TourGridText")))
            .Coachmark("wsAddRow", s => s
                .Placement(Side.Bottom)
                .Title(Localizer.Get("TourAddRowTitle"))
                .Text(Localizer.Get("TourAddRowText")))
            .Coachmark("wsSearch", s => s
                .Placement(Side.Bottom)
                .Title(Localizer.Get("TourSearchTitle"))
                .Text(Localizer.Get("TourSearchText")))
            .Coachmark("wsSwitchJson", s => s
                .Placement(Side.Bottom)
                .Title(Localizer.Get("TourSwitchJsonTitle"))
                .Text(Localizer.Get("TourSwitchJsonText")))

            // ---- File explorer: reveal the panel, then walk its contents ----
            .Coachmark("fileExplorerBtn", s => s
                .Placement(Side.Top)
                .Title(Localizer.Get("TourFileExplorerTitle"))
                .Text(Localizer.Get("TourFileExplorerText"))
                // Open the side panel so the next steps have something to spotlight.
                .OnEnter(() =>
                {
                    if (vm != null)
                    {
                        explorerWasVisible = vm.FileExplorer.IsVisible;
                        vm.FileExplorer.IsVisible = true;
                    }
                }))
            .Coachmark("explorerPanel", s => s
                .Placement(Side.Right)
                .Title(Localizer.Get("TourExplorerPanelTitle"))
                .Text(Localizer.Get("TourExplorerPanelText")))
            .Coachmark("explorerOpenFolder", s => s
                .Placement(Side.Right)
                .Title(Localizer.Get("TourExplorerOpenTitle"))
                .Text(Localizer.Get("TourExplorerOpenText"))
                // Leaving the explorer steps: put the panel back the way we found it.
                .OnExit(() =>
                {
                    if (vm != null)
                        vm.FileExplorer.IsVisible = explorerWasVisible;
                }))

            // ---- Window chrome (MainWindow) ----
            .Coachmark("notificationsBtn", s => s
                .Placement(Side.Bottom)
                .Title(Localizer.Get("TourNotificationsTitle"))
                .Text(Localizer.Get("TourNotificationsText")))
            .Coachmark("themeBtn", s => s
                .Placement(Side.Bottom)
                .Title(Localizer.Get("TourThemeTitle"))
                .Text(Localizer.Get("TourThemeText")))
            .Coachmark("languageBtn", s => s
                .Placement(Side.Bottom)
                .Title(Localizer.Get("TourLanguageTitle"))
                .Text(Localizer.Get("TourLanguageText")))
            .Coachmark("githubBtn", s => s
                .Placement(Side.Bottom)
                .Title(Localizer.Get("TourGithubTitle"))
                .Text(Localizer.Get("TourGithubText")))

            // ---- Replay ----
            .Coachmark("tourHelp", s => s
                .Placement(Side.Bottom)
                .Title(Localizer.Get("TourHelpStepTitle"))
                .Text(Localizer.Get("TourHelpStepText")))
            .Build();
    }
}
