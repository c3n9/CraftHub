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
        public ICommand OpenGenerateFoldersindow { get; set; }
        public ICommand UploadTemplateCommand { get; set; }
        public ICommand ExportJsonFileCommand { get; set; }
        public ICommand ImportJsonFileCommand { get; set; }

        private PropertiesViewModel propertiesViewModel;

        public MainWindowViewModel()
        {
            MinimizeWindowCommand = new DelegateCommand(OnMinimizeWindowCommand);
            MaximizeWindowCommand = new DelegateCommand(OnMaximizeWindowCommand);
            CloseWindowCommand = new DelegateCommand(OnCloseWindowCommand);
            OpenGenerateFoldersindow = new DelegateCommand(OnOpenGenerateLessonsindow);
            UploadTemplateCommand = new DelegateCommand(UploadTemplate);
            ImportJsonFileCommand = new DelegateCommand(OnImportJsonFileCommand);
            ExportJsonFileCommand = new DelegateCommand(OnExportJsonFileCommand);

            App.MainWindowViewModel = this;
            MainFrameSource = new WorkingAreaView();

        }
        private void OnOpenGenerateLessonsindow(object paramenter)
        {
            new GenerationFoldersWinodow().ShowDialog();
        }
        private void OnExportJsonFileCommand(object paramenter)
        {
            var exportJsonString = JsonConvert.SerializeObject(App.WorkingWithJsonViewModel.DataTable, Formatting.Indented);
            if (!string.IsNullOrWhiteSpace(exportJsonString))
            {
                var dialog = new SaveFileDialog() { Filter = ".json | *.json" };
                if (dialog.ShowDialog().GetValueOrDefault())
                {
                    File.WriteAllText(dialog.FileName, exportJsonString, Encoding.UTF8);
                    MessageBox.Show("Successful export", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("Import json first");
                return;
            }
        }
        private void OnImportJsonFileCommand(object paramenter)
        {
            var dialog = new OpenFileDialog() { Filter = ".json | *.json" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                App.jsonString = File.ReadAllText(dialog.FileName);
                JArray jsonArray = new JArray();
                if (!string.IsNullOrWhiteSpace(App.jsonString))
                {
                    jsonArray = JArray.Parse(App.jsonString);
                    foreach (var jsonItem in jsonArray)
                    {
                        var dataRow = App.WorkingAreaViewModel.DataTable.NewRow();
                        var jsonObject = jsonItem as JObject;

                        foreach (var property in jsonObject.Properties())
                        {
                            var columnName = property.Name;
                            if (App.WorkingAreaViewModel.DataTable.Columns.Contains(columnName))
                            {
                                var columnValue = property.Value.ToObject<object>();
                                dataRow[columnName] = columnValue;
                            }
                        }
                        App.WorkingAreaViewModel.DataTable.Rows.Add(dataRow);
                    }
                }
            }
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
