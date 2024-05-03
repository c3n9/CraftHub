using CraftHub.ViewModels.Base;
using CraftHub.ViewModels.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Newtonsoft.Json;
using System.Data;
using System.Windows.Forms;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace CraftHub.ViewModels
{
    internal class AddNewElementWindowViewModel : BaseViewModel
    {
        public ICommand MinimizeWindowCommand { get; set; }
        public ICommand CloseWindowCommand { get; set; }
        public ICommand MaximizeWindowCommand { get; set; }
        public ICommand SaveDataCommand { get; set; }
        public StackPanel NewStackPanel { get; set; }
        private ObservableCollection<UIElement> _uIElemetsCollection;
        public ObservableCollection<UIElement> UIElemetsCollection
        {
            get { return _uIElemetsCollection; }
            set
            {
                _uIElemetsCollection = value;
                OnPropertyChanged(nameof(UIElemetsCollection));
            }
        }
        public AddNewElementWindowViewModel()
        {
            MinimizeWindowCommand = new DelegateCommand(OnMinimizeWindowCommand);
            MaximizeWindowCommand = new DelegateCommand(OnMaximizeWindowCommand);
            CloseWindowCommand = new DelegateCommand(OnCloseWindowCommand);
            SaveDataCommand = new DelegateCommand(OnSaveDataCommand);
            GenerationEditForm();
        }
        private void GenerationEditForm()
        {
            var listElementsName = App.WorkingAreaViewModel.Properties.Select(x => x.Name).ToList();
            var listValueTypes = App.WorkingAreaViewModel.Properties.Select(x => x.Type).ToList();
            if (!App.IsAdding)
            {
                UIElemetsCollection = new ObservableCollection<UIElement>();

                var dataRow = App.DataRowView.Row;
                var json = JsonConvert.SerializeObject(dataRow.ItemArray);
                var values = JsonConvert.DeserializeObject<List<string>>(json);
                for (int i = 0; i < values.Count; i++)
                {
                    TextBlock textBlock = new TextBlock()
                    {
                        FontSize = 16,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    };
                    textBlock.Text = $"{listElementsName[i]} ({listValueTypes[i]})";
                    UIElemetsCollection.Add(textBlock);
                    if (listValueTypes[i].ToString() == typeof(bool).FullName)
                    {
                        System.Windows.Controls.CheckBox checkBox = new System.Windows.Controls.CheckBox()
                        {
                            Margin = new Thickness(0, 5, 0, 10)
                        };
                        checkBox.IsChecked = Convert.ToBoolean(values[i]);
                        UIElemetsCollection.Add(checkBox);
                        continue;
                    }
                    System.Windows.Controls.TextBox textBox = new System.Windows.Controls.TextBox()
                    {
                        Width = 300,
                        TextWrapping = TextWrapping.Wrap,
                        Height = 100,
                        Style = (Style)System.Windows.Application.Current.FindResource("MaterialDesignOutlinedTextBox"),
                        Padding = new Thickness(5),
                        FontSize = 16,
                        Margin = new Thickness(0, 5, 0, 10)
                    };
                    textBox.Text = values[i];
                    UIElemetsCollection.Add(textBox);

                }
            }
            else
            {
                UIElemetsCollection = new ObservableCollection<UIElement>();

                for (int i = 0; i < listElementsName.Count; i++)
                {
                    TextBlock textBlock = new TextBlock()
                    {
                        FontSize = 16,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        Text = $"{listElementsName[i]}({listValueTypes[i]})"
                    };
                    UIElemetsCollection.Add(textBlock);
                    var c = listValueTypes[i].ToString();
                    if (listValueTypes[i].ToString() == typeof(bool).FullName)
                    {
                        System.Windows.Controls.CheckBox checkBox = new System.Windows.Controls.CheckBox()
                        {
                            Margin = new Thickness(0, 5, 0, 10),
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        };
                        UIElemetsCollection.Add(checkBox);
                    }
                    else
                    {
                        System.Windows.Controls.TextBox textBox = new System.Windows.Controls.TextBox()
                        {
                            Width = 300,
                            TextWrapping = TextWrapping.Wrap,
                            Height = 100,
                            Style = (Style)System.Windows.Application.Current.FindResource("MaterialDesignOutlinedTextBox"),
                            Padding = new Thickness(5),
                            FontSize = 16,
                            Margin = new Thickness(0, 5, 0, 10)
                        };
                        UIElemetsCollection.Add(textBox);
                    }
                }
            }
        }
        private void OnSaveDataCommand(object paramenter)
        {
            try
            {
                if (App.IsAdding)
                {
                    DataRow newRow = App.DataRowView.Row.Table.NewRow();
                    for (int i = 0; i < UIElemetsCollection.Count; i += 2)
                    {
                        var textBox = UIElemetsCollection[i + 1] as System.Windows.Controls.TextBox;
                        var checkBox = UIElemetsCollection[i + 1] as System.Windows.Controls.CheckBox;
                        if (UIElemetsCollection[i] is TextBlock textBlock && (textBox is System.Windows.Controls.TextBox || checkBox is System.Windows.Controls.CheckBox))
                        {
                            var columnName = textBlock.Text;
                            string value = string.Empty;
                            if (textBox != null)
                            {
                                value = textBox.Text;
                            }
                            else
                            {
                                value = checkBox.IsChecked.ToString();
                            }
                            newRow[columnName] = value;
                        }
                    }
                    App.DataRowView.Row.Table.Rows.Add(newRow);
                }
                else
                {
                    for (int i = 0; i < UIElemetsCollection.Count; i += 2)
                    {
                        var textBox = UIElemetsCollection[i + 1] as System.Windows.Controls.TextBox;
                        var checkBox = UIElemetsCollection[i + 1] as System.Windows.Controls.CheckBox;
                        if (UIElemetsCollection[i] is TextBlock textBlock && (textBox is System.Windows.Controls.TextBox || checkBox is System.Windows.Controls.CheckBox))
                        {
                            var columnName = textBlock.Text;
                            string modifiedValue = string.Empty;
                            if (textBox != null)
                            {
                                modifiedValue = textBox.Text;
                            }
                            else
                            {
                                modifiedValue = checkBox.IsChecked.ToString();
                            }
                            App.DataRowView[columnName] = modifiedValue;
                        }
                    }
                }
                App.AddNewElementWindow.Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
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
