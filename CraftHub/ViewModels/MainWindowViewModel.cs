using CraftHub.Models;
using CraftHub.ViewModels.Base;
using CraftHub.ViewModels.Commands;
using CraftHub.Views;
using Microsoft.CodeAnalysis;
using Microsoft.CSharp;
using Microsoft.Win32;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
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
        public ICommand OpenGenerateFoldersindow { get; set; }
        public ICommand UploadTemplateCommand { get; set; }

        private PropertiesViewModel propertiesViewModel;

        public MainWindowViewModel()
        {
            MinimizeWindowCommand = new DelegateCommand(OnMinimizeWindowCommand);
            MaximizeWindowCommand = new DelegateCommand(OnMaximizeWindowCommand);
            CloseWindowCommand = new DelegateCommand(OnCloseWindowCommand);
            OpenGenerateFoldersindow = new DelegateCommand(OnOpenGenerateLessonsindow);
            UploadTemplateCommand = new DelegateCommand(UploadTemplate);

            App.MainWindowViewModel = this;
            MainFrameSource = new PropertiesView();

        }
        private void OnOpenGenerateLessonsindow(object paramenter)
        {
            new GenerationFoldersWinodow().ShowDialog();
        }
        private void UploadTemplate(object paramenter)
        {
            var dialog = new OpenFileDialog() { Filter = ".cs | *.cs" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                string code = System.IO.File.ReadAllText(dialog.FileName);
                CompileAndLoadCode(code);
            }
        }

        private void CompileAndLoadCode(string code)
        {
            App.PropertiesViewModel.Properties.Clear();
            // Используем провайдер компиляции C# кода
            CSharpCodeProvider provider = new CSharpCodeProvider();
            CompilerParameters parameters = new CompilerParameters();
            // Получаем сборки, доступные в текущем домене приложения
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(a => a.Location).ToArray();
            parameters.ReferencedAssemblies.AddRange(assemblies);
            parameters.ReferencedAssemblies.Add("System.Runtime.dll");
            // Компилируем код
            CompilerResults results = provider.CompileAssemblyFromSource(parameters, code);
            string errorMessage = string.Empty;
            if (results.Errors.HasErrors)
            {
                foreach (CompilerError error in results.Errors)
                {
                    errorMessage += $"Error in line {error.Line}: {error.ErrorText}\n";
                }
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    MessageBox.Show(errorMessage);
                    return;
                }
            }
            else
            {
                // Загружаем сборку
                Assembly assembly = results.CompiledAssembly;
                foreach (Type type in assembly.GetTypes())
                {
                    dynamic instance = Activator.CreateInstance(type);
                    foreach (var propertyInDynamic in instance.GetType().GetProperties())
                    {
                        App.PropertiesViewModel.Properties.Add(new PropertyModel { Name = propertyInDynamic.Name, Type = propertyInDynamic.PropertyType }); 
                    }
                }
            }
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
