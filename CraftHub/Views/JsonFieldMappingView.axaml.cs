using System.Collections.Generic;
using System.Linq;
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
            Close(vm.Fields.ToList());
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null as List<JsonFieldMapping>);
    }
}
