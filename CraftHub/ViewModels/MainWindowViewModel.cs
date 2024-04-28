using CraftHub.Models;
using CraftHub.ViewModels.Base;
using CraftHub.ViewModels.Commands;
using CraftHub.Views;
using Microsoft.CodeAnalysis.CSharp;
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
            try
            {
                Assembly assembly = Compile(code);
                if (assembly == null)
                {
                    return;
                }

                foreach (Type type in assembly.GetTypes())
                {
                    dynamic instance = Activator.CreateInstance(type);
                    foreach (var propertyInDynamic in instance.GetType().GetProperties())
                    {
                        App.PropertiesViewModel.Properties.Add(new PropertyModel()
                        {
                            Name = propertyInDynamic.Name.ToString(),
                            Type = propertyInDynamic.PropertyType
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            
        }
        private static Assembly Compile(string code)
        {
            var syntaxTree = SyntaxFactory.ParseSyntaxTree(code);
            var references = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(a => MetadataReference.CreateFromFile(a.Location));
            var compilation = CSharpCompilation.Create("AssemblyName",
                Enumerable.Repeat(syntaxTree, 1),
                references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var memoryStream = new MemoryStream())
            {
                var emitResult = compilation.Emit(memoryStream);

                if (emitResult.Success)
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    return Assembly.Load(memoryStream.ToArray());
                }
                else
                {
                    return null;
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
