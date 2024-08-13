using CraftHub.Models;
using CraftHub.ViewModels.Base;
using CraftHub.ViewModels.Commands;
using CraftHub.Views;
using Microsoft.CodeAnalysis;
using Microsoft.CSharp;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CraftHub.ViewModels
{
    internal class MainWindowViewModel : BaseViewModel
    {
        private Page mainFrameSource;
        public Page MainFrameSource
        {
            get
            {
                return mainFrameSource;
            }
            set
            {
                mainFrameSource = value;
                OnPropertyChanged();
            }
        }

        public ICommand MinimizeWindowCommand { get; set; }
        public ICommand CloseWindowCommand { get; set; }
        public ICommand MaximizeWindowCommand { get; set; }

        public MainWindowViewModel()
        {
            MinimizeWindowCommand = new DelegateCommand(OnMinimizeWindowCommand);
            MaximizeWindowCommand = new DelegateCommand(OnMaximizeWindowCommand);
            CloseWindowCommand = new DelegateCommand(OnCloseWindowCommand);

            App.MainWindowViewModel = this;
            MainFrameSource = new WorkingAreaView();

        }
        

        private void OnMinimizeWindowCommand(object paramenter)
        {
            (paramenter as Window).WindowState = WindowState.Minimized;
        }
        private void OnMaximizeWindowCommand(object paramenter)
        {
            if ((paramenter as Window).WindowState == WindowState.Maximized)
                (paramenter as Window).WindowState = WindowState.Normal;
            else
                (paramenter as Window).WindowState = WindowState.Maximized;
        }
        private void OnCloseWindowCommand(object paramenter)
        {
            (paramenter as Window).Close();
        }
    }
}
