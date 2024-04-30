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

namespace CraftHub.ViewModels
{
    internal class AddNewElementWindowViewModel : BaseViewModel
    {
        public ICommand MinimizeWindowCommand { get; set; }
        public ICommand CloseWindowCommand { get; set; }
        public ICommand MaximizeWindowCommand { get; set; }
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
            GenerationEditForm();
        }
        private void GenerationEditForm()
        {
            var listElementsName = App.PropertiesViewModel.Properties.Select(x => x.Name).ToList();
            var listValueTypes = App.PropertiesViewModel.Properties.Select(x => x.Type).ToList();
            //if (!_isAdding)
            //{
            //    var dataRow = _selectedDataRowView.Row;
            //    var json = JsonConvert.SerializeObject(dataRow.ItemArray);
            //    var values = JsonConvert.DeserializeObject<List<string>>(json);

            //    for (int i = 0; i < values.Count; i++)
            //    {
            //        TextBlock textBlock = new TextBlock()
            //        {
            //            FontSize = 16,
            //            HorizontalAlignment = HorizontalAlignment.Center,
            //        };
            //        textBlock.Text = listElementsName[i];
            //        SPElements.Children.Add(textBlock);
            //        if (listValueTypes[i] == typeof(bool).Name)
            //        {
            //            CheckBox checkBox = new CheckBox()
            //            {
            //                Margin = new Thickness(0, 5, 0, 10)
            //            };
            //            checkBox.IsChecked = Convert.ToBoolean(values[i]);
            //            SPElements.Children.Add(checkBox);
            //            continue;
            //        }
            //        TextBox textBox = new TextBox()
            //        {
            //            Width = 200,
            //            TextWrapping = TextWrapping.Wrap,
            //            Height = 100,
            //            Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedTextBox"),
            //            Padding = new Thickness(5),
            //            FontSize = 16,
            //            Margin = new Thickness(0, 5, 0, 10)
            //        };
            //        textBox.Text = values[i];
            //        SPElements.Children.Add(textBox);

            //    }
            //}
            //else
            //{
            UIElemetsCollection = new ObservableCollection<UIElement>();

            for (int i = 0; i < listElementsName.Count; i++)
            {
                TextBlock textBlock = new TextBlock()
                {
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Text = listElementsName[i]
                };
                UIElemetsCollection.Add(textBlock);
                var c = listValueTypes[i].ToString();
                if (listValueTypes[i].ToString() == typeof(bool).FullName)
                {
                    CheckBox checkBox = new CheckBox()
                    {
                        Margin = new Thickness(0, 5, 0, 10),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    };
                    UIElemetsCollection.Add(checkBox);
                }
                else
                {
                    TextBox textBox = new TextBox()
                    {
                        Width = 200,
                        TextWrapping = TextWrapping.Wrap,
                        Height = 100,
                        Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedTextBox"),
                        Padding = new Thickness(5),
                        FontSize = 16,
                        Margin = new Thickness(0, 5, 0, 10)
                    };
                    UIElemetsCollection.Add(textBox);
                }
            }
            //}
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
