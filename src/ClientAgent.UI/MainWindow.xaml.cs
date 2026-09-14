using System.Windows;
using ClientAgent.UI.ViewModels;

namespace ClientAgent.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
