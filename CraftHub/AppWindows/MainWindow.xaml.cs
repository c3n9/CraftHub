using CraftHub.AppWindows;
using CraftHub.Pages;
using CraftHub.Services;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
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
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CraftHub.AppWindows
{
    public partial class MainWindow : Window
    {
        private Color primaryColor = (Color)ColorConverter.ConvertFromString("#3f51b5");
        private Color secondaryColor = (Color)ColorConverter.ConvertFromString("#3f51b5");
        private bool isDragging = false;
        private Point startPoint;
        public event EventHandler<bool> AddInModalWindowCheckedChanged;
        public MainWindow()
        {
            InitializeComponent();
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            GlobalSettings.mainWindow = this;
            if (Properties.Settings.Default.IsDarkTheme)
            {
                SetDarkTheme();
            }
            else
            {
                SetLightTheme();
            }
            MainFrame.Navigate(new PropertiesPage());
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (GlobalSettings.jsonPage != null)
                {
                    Option.ViewSurchOption();
                    e.Handled = true;
                }
            }
        }

        private void MIImportClass_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog() { Filter = ".cs | *.cs" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                this.Title = $"CraftHub — {System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)}";
                string code = System.IO.File.ReadAllText(dialog.FileName);
                CompileAndLoadCode(code);
                GlobalSettings.RefreshProperties();
            }
        }

        private void MIImportJsonFile_Click(object sender, RoutedEventArgs e)
        {
            if (GlobalSettings.dictionary.Count == 0)
            {
                MessageBox.Show("Upload a template or add properties");
                return;
            }
            var dialog = new OpenFileDialog() { Filter = ".json | *.json" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                GlobalSettings.jsonString = File.ReadAllText(dialog.FileName);
                GlobalSettings.DisplayDataInGrid();
            }
        }
        private void CompileAndLoadCode(string code)
        {
            GlobalSettings.dictionary = new Dictionary<string, dynamic>();
            GlobalSettings.jsonString = null;
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
                    GlobalSettings.generatedObject = instance;
                    foreach (var propertyInDynamic in instance.GetType().GetProperties())
                    {
                        GlobalSettings.dictionary.Add(propertyInDynamic.Name.ToString(), propertyInDynamic.PropertyType);
                    }
                }
            }
        }

        private void MIExportJsonFile_Click(object sender, RoutedEventArgs e)
        {
            var exportJsonString = JsonConvert.SerializeObject(GlobalSettings.dataTable, Formatting.Indented);
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
        private void MIAddInModalWindow_Checked(object sender, RoutedEventArgs e)
        {
            AddInModalWindowCheckedChanged?.Invoke(this, MIAddInModalWindow.IsChecked);
        }

        private void MIAddInModalWindow_Unchecked(object sender, RoutedEventArgs e)
        {
            AddInModalWindowCheckedChanged?.Invoke(this, MIAddInModalWindow.IsChecked);
        }

        private void MIViewJson_Checked(object sender, RoutedEventArgs e)
        {
            if (GlobalSettings.jsonPage != null && GlobalSettings.jsonPage.DGJsonData != null && GlobalSettings.jsonPage.DGJsonData.SelectedItem != null)
            {
                Option.ViewJsonFromTable();
            }
        }

        private void MIViewJson_Unchecked(object sender, RoutedEventArgs e)
        {
            if (GlobalSettings.jsonPage != null && GlobalSettings.jsonPage.DGJsonData != null && GlobalSettings.jsonPage.DGJsonData.SelectedItem != null)
            {
                Grid.SetColumnSpan(GlobalSettings.jsonPage.DGJsonData, 2);
                GlobalSettings.jsonPage.TBJson.Visibility = Visibility.Collapsed;
                GlobalSettings.jsonPage.BCopyToClipboard.Visibility = Visibility.Collapsed;

            }

        }

        private void MIGenerationFolders_Click(object sender, RoutedEventArgs e)
        {
            new GenerationFoldersWindow().ShowDialog();
        }

        private void MILessonConstuctor_Click(object sender, RoutedEventArgs e)
        {
            new LessonConstructorWindow().ShowDialog();
        }
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                isDragging = true;
                startPoint = e.GetPosition(this);
            }
        }

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                Point endPoint = e.GetPosition(this);
                double offsetX = endPoint.X - startPoint.X;
                double offsetY = endPoint.Y - startPoint.Y;

                Left += offsetX;
                Top += offsetY;
            }
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                isDragging = false;
            }
        }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            // Минимизация окна
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            // Максимизация или восстановление окна
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Закрытие окна
            this.Close();
        }

        private void ToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            if (Properties.Settings.Default.IsDarkTheme)
            {
                Properties.Settings.Default.IsDarkTheme = false;
                Properties.Settings.Default.Save();
                SetLightTheme();
            }
            else
            {
                Properties.Settings.Default.IsDarkTheme = true;
                Properties.Settings.Default.Save();
                SetDarkTheme();
            }
        }

        private void ToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            if (Properties.Settings.Default.IsDarkTheme)
            {
                Properties.Settings.Default.IsDarkTheme = false;
                Properties.Settings.Default.Save();
                SetLightTheme();
            }
            else
            {
                Properties.Settings.Default.IsDarkTheme = true;
                Properties.Settings.Default.Save();
                SetDarkTheme();
            }

        }
        public void SetDarkTheme()
        {
            var paletteHelper = new PaletteHelper();
            ITheme theme = paletteHelper.GetTheme();

            // Установить сохранённые цвета
            var newTheme = new Theme();
            newTheme.SetBaseTheme(Theme.Dark);
            newTheme.PrimaryMid = new ColorPair(primaryColor);
            newTheme.SecondaryMid = new ColorPair(secondaryColor);

            paletteHelper.SetTheme(newTheme);
        }
        public void SetLightTheme()
        {
            var paletteHelper = new PaletteHelper();
            ITheme theme = paletteHelper.GetTheme();

            // Установить сохранённые цвета
            var newTheme = new Theme();
            newTheme.SetBaseTheme(Theme.Light);
            newTheme.PrimaryMid = new ColorPair(primaryColor);
            newTheme.SecondaryMid = new ColorPair(secondaryColor);

            paletteHelper.SetTheme(newTheme);
        }
    }
}
