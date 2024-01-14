using CraftHub.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace CraftHub.AppWindows
{
    /// <summary>
    /// Логика взаимодействия для LessonConstructorWindow.xaml
    /// </summary>
    public partial class LessonConstructorWindow : Window
    {
        private bool isDragging = false;
        private bool isResizing = false;
        private Point resizeStartPoint;
        private double originalWidth;
        private double originalHeight;
        public LessonConstructorWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Обработчик события нажатия левой кнопки мыши для начала перетаскивания.
        /// </summary>
        private void Dragging_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Проверяем, была ли нажата клавиша Shift при нажатии левой кнопки мыши.
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                isDragging = true;
                // Захватываем мышь, чтобы отслеживать движение, когда кнопка мыши удерживается.
                (sender as UIElement).CaptureMouse();
            }
        }

        /// <summary>
        /// Обработчик события отпускания левой кнопки мыши для окончания перетаскивания.
        /// </summary>
        private void Dragging_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Завершаем перетаскивание при отпускании левой кнопки мыши.
            isDragging = false;
            (sender as UIElement).ReleaseMouseCapture();
        }

        /// <summary>
        /// Обработчик события перемещения мыши для обновления положения элемента во время перетаскивания.
        /// </summary>
        private void Dragging_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                // Вычисляем новые координаты для элемента в зависимости от текущего положения мыши.
                double mouseX = e.GetPosition(this).X - ((UIElement)sender).RenderSize.Width / 2;
                double mouseY = e.GetPosition(this).Y - ((UIElement)sender).RenderSize.Height / 2;

                // Устанавливаем новые координаты элемента.
                Canvas.SetLeft((UIElement)sender, mouseX);
                Canvas.SetTop((UIElement)sender, mouseY);
            }
        }
        /// <summary>
        /// Обработчик события нажатия правой кнопки мыши для изменения размера.
        /// </summary>
        private void Resizing_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Устанавливаем флаг, указывающий, что начат процесс изменения размера.
            isResizing = true;

            // Запоминаем начальную позицию, где началось изменение размера.
            resizeStartPoint = e.GetPosition(sender as UIElement);

            // Запоминаем оригинальную ширину и высоту UIElement.
            originalWidth = (sender as UIElement).RenderSize.Width;
            originalHeight = (sender as UIElement).RenderSize.Height;

            // Захватываем мышь для отслеживания последующих движений мыши.
            (sender as UIElement).CaptureMouse();
        }
        /// <summary>
        /// Обработчик события отпускания правой кнопки мыши после изменения размера.
        /// </summary>
        private void Resizing_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Сбрасываем флаг изменения размера, так как изменение завершено.
            isResizing = false;

            // Освобождаем захват мыши, чтобы прекратить отслеживание дальнейших движений мыши.
            (sender as UIElement).ReleaseMouseCapture();
        }
        /// <summary>
        /// Обработчик события перемещения мыши во время изменения размера.
        /// </summary>
        private void Resizing_MouseMove(object sender, MouseEventArgs e)
        {
            // Проверяем, идет ли в данный момент процесс изменения размера.
            if (isResizing)
            {
                // Вычисляем изменение позиции по X и Y относительно начальной точки.
                double deltaX = e.GetPosition(CWorkingArea).X - resizeStartPoint.X;
                double deltaY = e.GetPosition(CWorkingArea).Y - resizeStartPoint.Y;

                // Вычисляем новую ширину и высоту на основе изменений.
                double newWidth = Math.Max(0, originalWidth + deltaX);
                double newHeight = Math.Max(0, originalHeight + deltaY);

                // Обновляем ширину и минимальную высоту изменяемого FrameworkElement.
                (sender as FrameworkElement).Width = newWidth;
                (sender as FrameworkElement).MinHeight = newHeight;
            }
        }
        private void BAddImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog() { Filter = ".png, .jpg, .jpeg | *.png; *.jpg; *.jpeg;" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                // Создаем контекстное меню
                ContextMenu contextMenu = new ContextMenu();
                // Создаем пункт меню "Удалить"
                MenuItem deleteMenuItem = new MenuItem();
                deleteMenuItem.Header = "Delete";
                Image iconImage = new Image();
                iconImage.Source = new BitmapImage(new Uri("/Recourses/delete.png", UriKind.Relative));
                deleteMenuItem.Icon = iconImage;
                deleteMenuItem.Click += MIDelete_Click;
                // Добавляем пункт меню в контекстное меню
                contextMenu.Items.Add(deleteMenuItem);

                var photoInBytes = File.ReadAllBytes(dialog.FileName);
                Image image = new Image() 
                { 
                    Source = MyTools.BytesToImage(photoInBytes), 
                    Width = 100, 
                    Height = 100, 
                    ContextMenu = contextMenu 
                };
                image.MouseLeftButtonDown += Dragging_MouseLeftButtonDown;
                image.MouseLeftButtonUp += Dragging_MouseLeftButtonUp;
                image.MouseMove += Dragging_MouseMove;

                image.MouseRightButtonDown += Resizing_MouseRightButtonDown;
                image.MouseRightButtonUp += Resizing_MouseRightButtonUp;
                image.MouseMove += Resizing_MouseMove;

                CWorkingArea.Children.Add(image);
            }
        }

        private void BAddTextBox_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TBLabel.Text))
            {
                MessageBox.Show("Enter text", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Создаем контекстное меню
            ContextMenu contextMenu = new ContextMenu();
            // Создаем пункт меню "Удалить"
            MenuItem deleteMenuItem = new MenuItem();
            deleteMenuItem.Header = "Delete";
            Image iconImage = new Image();
            iconImage.Source = new BitmapImage(new Uri("/Recourses/delete.png", UriKind.Relative));
            deleteMenuItem.Icon = iconImage;
            deleteMenuItem.Click += MIDelete_Click;
            // Добавляем пункт меню в контекстное меню
            contextMenu.Items.Add(deleteMenuItem);

            var textBlock = new TextBlock() 
            { 
                Width = 200, 
                Height = 100, 
                Text = $"{TBLabel.Text}", 
                TextWrapping = TextWrapping.Wrap, 
                ContextMenu = contextMenu
            };
            textBlock.MouseLeftButtonDown += Dragging_MouseLeftButtonDown;
            textBlock.MouseLeftButtonUp += Dragging_MouseLeftButtonUp;
            textBlock.MouseMove += Dragging_MouseMove;

            textBlock.MouseRightButtonDown += Resizing_MouseRightButtonDown;
            textBlock.MouseRightButtonUp += Resizing_MouseRightButtonUp;
            textBlock.MouseMove += Resizing_MouseMove;

            CWorkingArea.Children.Add(textBlock);
        }
        private void MIDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                ContextMenu contextMenu = menuItem.Parent as ContextMenu;
                if (contextMenu.PlacementTarget is Image image)
                    CWorkingArea.Children.Remove(image);
                if (contextMenu.PlacementTarget is TextBlock textBlock)
                    CWorkingArea.Children.Remove(textBlock);
            }
        }

        private void BClear_Click(object sender, RoutedEventArgs e)
        {
            CWorkingArea.Children.Clear();
        }
    }
}
