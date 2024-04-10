using CraftHub.ViewModels.Commands;
using CraftHub.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CraftHub.ViewModels
{
    internal class WorkingWithJsonViewModel
    {
        public ICommand NavigateToPropertiesViewCommand { get; set; }
        public WorkingWithJsonViewModel()
        {
            NavigateToPropertiesViewCommand = new DelegateCommand(OnNavigateToPropertiesViewCommand);
        }
        private void OnNavigateToPropertiesViewCommand(object parameter)
        {
            App.mainWindowViewModel.MainFrameSource = new PropertiesView();
        }
    }
}
