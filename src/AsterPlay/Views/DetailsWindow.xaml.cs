using AsterPlay.ViewModels;

namespace AsterPlay.Views;

public partial class DetailsWindow : Window
{
    public DetailsWindow(MediaCardViewModel item)
    {
        InitializeComponent();
        DataContext = item;
        Owner = Application.Current.MainWindow;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
