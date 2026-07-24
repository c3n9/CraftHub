using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CraftHub.Domain.Models;
using CraftHub.Models;
using CraftHub.ViewModels;

namespace CraftHub.Views;

public partial class JsonFieldMappingView : Window
{
    public JsonFieldMappingView()
    {
        InitializeComponent();

        ConfirmButton.Click += OnConfirm;
        CancelButton.Click += OnCancel;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (DataContext is JsonFieldMappingViewModel vm)
        {
            // Not vm.Fields — that is the visible tree; only the leaves of the user's
            // expansion choice become columns.
            Close(vm.GetResultFields());
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null as List<JsonFieldMapping>);
    }
}
