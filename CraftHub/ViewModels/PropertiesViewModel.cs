using CraftHub.ViewModels.Base;
using CraftHub.ViewModels.Commands;
using CraftHub.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CraftHub.ViewModels
{
    internal class PropertiesViewModel : BaseViewModel
    {
        private MainWindowViewModel _mainWindowViewModel;
        public ICommand NavigateToWorkingWithJsonViewCommand { get; set; }

        public PropertiesViewModel()
        {
            NavigateToWorkingWithJsonViewCommand = new DelegateCommand(OnNavigateToWorkingWithJsonViewCommand);
        }


        private void OnNavigateToWorkingWithJsonViewCommand(object parameter)
        {
            App.mainWindowViewModel.MainFrameSource = new WorkingWithJsonView();
        }
    }
}
