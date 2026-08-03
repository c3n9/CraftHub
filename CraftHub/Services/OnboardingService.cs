using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Coachlight.Avalonia;
using Coachlight.Avalonia.Building;
using Coachlight.Avalonia.Enums;
using Coachlight.Avalonia.Models;
using Coachlight.Avalonia.Persistence;
using CraftHub.Helpers;
using CraftHub.ViewModels;
using CraftHub.Views;

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
    // v2 adds the JSON editor, recent folders and releases steps.
    // v3 adds undo history, the search filter toggle, and Find & Replace.
    private const string AppTourId = "app-overview-v3";

    // JsonProgressStore persists completion to a JSON file under the OS application-data folder.
    private readonly IProgressStore _store = new JsonProgressStore();

    public void ShowAppTour(Visual anchor, bool force = false)
    {
        // The anchor is the main window; its DataContext drives the file-explorer step's
        // reveal/restore. A null vm just means those steps skip the toggle (still shown).
        var vm = (anchor as Control)?.DataContext as MainWindowViewModel;
        anchor.StartTour(BuildAppTour(vm, anchor), _store, force);
    }

    // A single long tour over the whole app: welcome → workspace editor → JSON editor →
    // file explorer → window chrome → help. Every target is present on startup (the app always
    // opens with one table-mode workspace), so all coachmarks resolve. Strings are pulled live
    // so the tour follows the current language.
    private static Tour BuildAppTour(MainWindowViewModel? vm, Visual anchor)
    {
        Control? ActiveWs() =>
            anchor.GetVisualDescendants()
                .OfType<WorkspaceView>()
                .FirstOrDefault(v => v.DataContext == vm?.SelectedWorkspace);

        Control? Mark(string id) => ActiveWs() is { } ws ? FindCoachmark(ws, id) : null;

        // Remembers whether the explorer panel was open before the tour so we can restore it
        // when the user navigates past the explorer steps.
        var explorerWasVisible = false;

        // The JSON-mode steps' targets only exist while the workspace is in JSON mode (and vice
        // versa for the table-mode steps around them). Each step that needs a particular mode
        // asserts it on entry — a no-op if already correct — so the right target is guaranteed
        // to be there however the step is reached (first pass, or Back from a later step).
        void EnsureJsonMode()
        {
            if (vm?.SelectedWorkspace is { IsJsonEditorMode: false } ws)
                ws.SwitchToJsonEditorCommand.Execute(null);
        }

        void EnsureTableMode()
        {
            if (vm?.SelectedWorkspace is { IsJsonEditorMode: true } ws)
                ws.SwitchToTableEditorCommand.Execute(null);
        }

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
                    .Interactive(false)
                    .Title(Localizer.Get("TourAddWorkspaceTitle"))
                    .Text(Localizer.Get("TourAddWorkspaceText")))

                // ---- Workspace editor (WorkspaceView) ----
                .Coachmark(() => Mark("wsSave"), s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourSaveTitle"))
                    .Text(Localizer.Get("TourSaveText")))
                .Coachmark(() => new[] { Mark("importJson"), Mark("exportJson") }, s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourImportExportTitle"))
                    .Text(Localizer.Get("TourImportExportText")))
                .Modal(s => s
                    .Title(Localizer.Get("TourDragDropTitle"))
                    .Text(Localizer.Get("TourDragDropText")))
                .Coachmark(() => Mark("wsAddProperty"), s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourAddPropertyTitle"))
                    .Text(Localizer.Get("TourAddPropertyText")))
                .Coachmark(() => Mark("wsSchema"), s => s
                    .Placement(Side.Right)
                    .Interactive(false)
                    .Title(Localizer.Get("TourSchemaTitle"))
                    .Text(Localizer.Get("TourSchemaText")))
                .Coachmark(() => Mark("wsGrid"), s => s
                    .Placement(Side.Left)
                    .Interactive(false)
                    .Title(Localizer.Get("TourGridTitle"))
                    .Text(Localizer.Get("TourGridText")))
                .Coachmark(() => Mark("wsAddRow"), s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourAddRowTitle"))
                    .Text(Localizer.Get("TourAddRowText")))
                .Coachmark(() => Mark("wsHistory"), s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourHistoryTitle"))
                    .Text(Localizer.Get("TourHistoryText")))
                .Coachmark(() => Mark("wsSearch"), s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourSearchTitle"))
                    .Text(Localizer.Get("TourSearchText")))
                .Coachmark(() => Mark("wsFilterToggle"), s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourFilterTitle"))
                    .Text(Localizer.Get("TourFilterText")))
                .Coachmark(() => Mark("wsReplace"), s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourReplaceTitle"))
                    .Text(Localizer.Get("TourReplaceText")))
                .Coachmark(() => Mark("wsSwitchJson"), s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourSwitchJsonTitle"))
                    .Text(Localizer.Get("TourSwitchJsonText"))
                    // Guarantees this step's own target (table-mode-only button) is there,
                    // whether it's reached going forward or by backing up out of the JSON steps.
                    .OnEnter(EnsureTableMode))

                // ---- JSON editor (JSON Mode) ----
                .Coachmark("wsJsonEditorArea", s => s
                    .Placement(Side.Top)
                    .Interactive(false)
                    .Title(Localizer.Get("TourJsonEditorTitle"))
                    .Text(Localizer.Get("TourJsonEditorText"))
                    .OnEnter(EnsureJsonMode))
                .Coachmark(new[] { "wsJsonPrettify", "wsJsonMinify" }, s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourJsonFormatTitle"))
                    .Text(Localizer.Get("TourJsonFormatText"))
                    .OnEnter(EnsureJsonMode))
                .Coachmark("wsJsonFind", s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourJsonFindTitle"))
                    .Text(Localizer.Get("TourJsonFindText"))
                    .OnEnter(EnsureJsonMode))
                .Coachmark("wsSwitchTable", s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourSwitchTableTitle"))
                    .Text(Localizer.Get("TourSwitchTableText"))
                    .OnEnter(EnsureJsonMode))

                // ---- File explorer: reveal the panel, then walk its contents ----
                .Coachmark("fileExplorerBtn", s => s
                    .Placement(Side.Top)
                    .Interactive(false)
                    .Title(Localizer.Get("TourFileExplorerTitle"))
                    .Text(Localizer.Get("TourFileExplorerText"))
                    // Leaving the JSON steps: back to table mode. Open the side panel so the
                    // next steps have something to spotlight.
                    .OnEnter(() =>
                    {
                        EnsureTableMode();
                        if (vm != null)
                        {
                            explorerWasVisible = vm.FileExplorer.IsVisible;
                            vm.FileExplorer.IsVisible = true;
                        }
                    }))
                .Coachmark("explorerPanel", s => s
                    .Placement(Side.Right)
                    .Interactive(false)
                    .Title(Localizer.Get("TourExplorerPanelTitle"))
                    .Text(Localizer.Get("TourExplorerPanelText")))
                .Coachmark("explorerRecentFolders", s => s
                    .Placement(Side.Right)
                    .Interactive(false)
                    .Title(Localizer.Get("TourRecentFoldersTitle"))
                    .Text(Localizer.Get("TourRecentFoldersText")))
                .Coachmark("explorerOpenFolder", s => s
                    .Placement(Side.Right)
                    .Interactive(false)
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
                    .Interactive(false)
                    .Title(Localizer.Get("TourNotificationsTitle"))
                    .Text(Localizer.Get("TourNotificationsText")))
                .Coachmark("themeBtn", s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourThemeTitle"))
                    .Text(Localizer.Get("TourThemeText")))
                .Coachmark("languageBtn", s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourLanguageTitle"))
                    .Text(Localizer.Get("TourLanguageText")))
                .Coachmark("releasesBtn", s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourReleasesTitle"))
                    .Text(Localizer.Get("TourReleasesText")))
                .Coachmark("githubBtn", s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourGithubTitle"))
                    .Text(Localizer.Get("TourGithubText")))

                // ---- Replay ----
                .Coachmark("tourHelp", s => s
                    .Placement(Side.Bottom)
                    .Interactive(false)
                    .Title(Localizer.Get("TourHelpStepTitle"))
                    .Text(Localizer.Get("TourHelpStepText")))
                .Build();
    }

    private static Control? FindCoachmark(Visual root, string id)
    {
        if (root is Control c && Coachlight.Avalonia.Targeting.Coachmark.GetId(c) == id)
            return c;

        foreach (var child in root.GetVisualChildren())
        {
            if (FindCoachmark(child, id) is { } found)
                return found;
        }

        return null;
    }
}