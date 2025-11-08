using System.Windows;

namespace TablePlacer
{
    public partial class ResizeDialog : Window
    {
        public int NewWidth { get; private set; }
        public int NewHeight { get; private set; }

        public ResizeDialog(int currentWidth, int currentHeight)
        {
            InitializeComponent();
            tbWidth.Text = currentWidth.ToString();
            tbHeight.Text = currentHeight.ToString();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(tbWidth.Text, out int width) &&
                int.TryParse(tbHeight.Text, out int height) &&
                width > 0 && height > 0)
            {
                NewWidth = width;
                NewHeight = height;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Введите корректные размеры!", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}