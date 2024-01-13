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
        public LessonConstructorWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Обработчик события нажатия левой кнопки мыши для начала перетаскивания.
        /// </summary>
        private void IDragging_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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
        private void IDragging_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Завершаем перетаскивание при отпускании левой кнопки мыши.
            isDragging = false;
            (sender as UIElement).ReleaseMouseCapture();
        }

        /// <summary>
        /// Обработчик события перемещения мыши для обновления положения элемента во время перетаскивания.
        /// </summary>
        private void IDragging_MouseMove(object sender, MouseEventArgs e)
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

        private void BAddImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog() { Filter = ".png, .jpg, .jpeg | *.png; *.jpg; *.jpeg;" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                var photoInBytes = File.ReadAllBytes(dialog.FileName);
                Image image = new Image() { Source = MyTools.BytesToImage(photoInBytes), Width = 100, Height = 100 };
                image.MouseLeftButtonDown += IDragging_MouseLeftButtonDown;
                image.MouseLeftButtonUp += IDragging_MouseLeftButtonUp;
                image.MouseMove += IDragging_MouseMove;
                CWorkingArea.Children.Add(image);
            }
        }

        private void BAddTextBox_Click(object sender, RoutedEventArgs e)
        {
            var textBlock = new TextBlock() { Width = 200, Height = 30, Text= "TEST_TEST_TEST_TEST_TEST" };
            textBlock.MouseLeftButtonDown += IDragging_MouseLeftButtonDown;
            textBlock.MouseLeftButtonUp += IDragging_MouseLeftButtonUp;
            textBlock.MouseMove += IDragging_MouseMove;
            CWorkingArea.Children.Add(textBlock);
        }
    }
}
