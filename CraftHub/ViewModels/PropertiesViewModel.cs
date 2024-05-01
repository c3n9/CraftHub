using CraftHub.Models;
using CraftHub.ViewModels.Base;
using CraftHub.ViewModels.Commands;
using CraftHub.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
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
        private Type _selectedType;

        public Type SelectedType
        {
            get { return _selectedType; }
            set
            {
                if (_selectedType != value)
                {
                    _selectedType = value;
                    OnPropertyChanged(nameof(SelectedType));
                }
            }
        }
        public ObservableCollection<PropertyModel> Properties { get; set; }
        public ObservableCollection<Type> AvailableTypes { get; set; }
        public ICommand AddPropertyCommand { get; private set; }
        public ICommand RemovePropertyCommand { get; private set; }
        public ICommand SaveTemplateCommand { get; private set; }
        public ICommand NavigateToWorkingWithJsonViewCommand { get; set; }

        public PropertiesViewModel()
        {
            App.PropertiesViewModel = this;

            Properties = new ObservableCollection<PropertyModel>();

            AvailableTypes = new ObservableCollection<Type>
            {
                typeof(int),
                typeof(float),
                typeof(bool),
                typeof(string),
                typeof(double),
                typeof(decimal)
            };

            AddPropertyCommand = new DelegateCommand(OnAddPropertyCommand);
            RemovePropertyCommand = new DelegateCommand(OnRemovePropertyCommand);
            SaveTemplateCommand = new DelegateCommand(OnSaveTemplateCommand);
            NavigateToWorkingWithJsonViewCommand = new DelegateCommand(OnNavigateToWorkingWithJsonViewCommand);
        }


        private void OnNavigateToWorkingWithJsonViewCommand(object parameter)
        {
            if(Properties.Count == 0)
            {
                MessageBox.Show("Upload a template or add properties", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var continueSave = MessageBox.Show("Do you want to save the class?", "Warning", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (continueSave == MessageBoxResult.OK)
                if (!SaveTemplate())
                    return;
            App.MainWindowViewModel.MainFrameSource = new WorkingWithJsonView();
        }
        private void OnAddPropertyCommand(object parameter)
        {
            var propertyName = parameter as string;
            if (!string.IsNullOrEmpty(propertyName))
            {
                Properties.Add(new PropertyModel() { Name = propertyName, Type = SelectedType });
            }
        }

        private void OnRemovePropertyCommand(object parameter)
        {
            if (Properties.Count > 0)
                Properties.RemoveAt(Properties.Count - 1);
        }

        private void OnSaveTemplateCommand(object parameter)
        {
            if (Properties.Count == 0)
            {
                MessageBox.Show("Upload a template or add properties", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            SaveTemplate();
        }
        private bool SaveTemplate()
        {
            var dialog = new SaveFileDialog() { Filter = ".cs | *.cs" };
            string exportClass = "using System;\r\nusing System.Collections.Generic;\r\nusing System.Linq;\r\nusing System.Text;\r\nusing System.Threading.Tasks;\r\n\r\nnamespace YourNamespace\r\n{\r\n    public class ";
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                exportClass += $"{System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)}{{\r\n";
                foreach (var element in Properties)
                {
                    exportClass += $"public {element.Name} {element.Type.Name} {{ get; set; }}\r\n";
                }
                exportClass += "}\r\n}";
                File.WriteAllText(dialog.FileName, exportClass);
                return true;
            }
            else
                return false;
        }
    }
}
